using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Protocol;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Mcp;
using PublicData.Tests.Support;

namespace PublicData.Tests.Integration;

/// <summary>
/// The same server in Streamable HTTP mode (the Docker Compose setup), reached through the same McpConnection
/// the chat and the API use, with the JWT the API signs.
/// </summary>
public sealed class McpHttpTests(McpHttpServerFixture server) : IClassFixture<McpHttpServerFixture>
{
    private const string ToolsList = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";

    [Fact]
    public async Task Connects_with_a_signed_token_lists_tools_and_calls_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await McpConnection.ConnectAsync(Settings(server.KeyFile), cancellationToken: ct);

        Assert.Equal("http", connection.Transport);
        Assert.Equal("jwt", connection.Authentication);
        Assert.Equal("public-data-mcp", connection.ServerName);
        Assert.Contains(connection.Tools, t => t.Name == "get_city_amendments");

        var result = await connection.Client.CallToolAsync("get_city_amendments",
            new Dictionary<string, object?> { ["city"] = "Campinas", ["state"] = "XX" }, cancellationToken: ct);

        Assert.True(result.IsError);
        Assert.Contains("UF 'XX' inválida", result.Content.OfType<TextContentBlock>().Single().Text);
    }

    [Fact]
    public async Task Valid_token_is_accepted()
    {
        using var response = await PostAsync(new McpTokenIssuer(new McpAuthSettings(), server.Key).GetToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("no-token")]
    [InlineData("expired")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-key")]
    [InlineData("alg-none")]
    public async Task Invalid_or_missing_token_gets_401(string scenario)
    {
        var token = scenario switch
        {
            "no-token" => null,
            // Expired five minutes ago: beyond the one-minute clock skew.
            "expired" => new McpTokenIssuer(new McpAuthSettings(), server.Key,
                new FixedTimeProvider(DateTimeOffset.UtcNow.AddMinutes(-10))).GetToken(),
            "wrong-audience" => new McpTokenIssuer(new McpAuthSettings { Audience = "outra-api" }, server.Key).GetToken(),
            "wrong-issuer" => new McpTokenIssuer(new McpAuthSettings { Issuer = "outro-emissor" }, server.Key).GetToken(),
            "wrong-key" => new McpTokenIssuer(new McpAuthSettings(), RandomNumberGenerator.GetBytes(48)).GetToken(),
            "alg-none" => UnsignedToken(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };

        using var response = await PostAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task Client_without_the_key_is_refused_with_a_clear_error()
    {
        var ex = await Assert.ThrowsAsync<McpStartupException>(() =>
            McpConnection.ConnectAsync(Settings(keyFile: null), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("401", ex.Message);
        Assert.Contains("SigningKeyFile", ex.Message);
    }

    [Fact]
    public async Task Health_stays_open_without_a_token()
    {
        using var http = new HttpClient();

        using var response = await http.GetAsync(server.Health, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Browser_origin_is_refused_to_prevent_dns_rebinding()
    {
        // Even with a valid token: the Origin check runs before authentication.
        using var response = await PostAsync(new McpTokenIssuer(new McpAuthSettings(), server.Key).GetToken(), origin: "http://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private McpSettings Settings(string? keyFile) => new()
    {
        Transport = "http",
        HttpUrl = server.Endpoint,
        Auth = new McpAuthSettings { SigningKeyFile = keyFile },
    };

    private async Task<HttpResponseMessage> PostAsync(string? token, string? origin = null)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
        {
            Content = new StringContent(ToolsList, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The classic attack: right issuer, audience and expiry, but "alg": "none" and no signature.</summary>
    private static string UnsignedToken()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var payload = Base64UrlEncoder.Encode($$"""{"iss":"{{McpAuthSettings.DefaultIssuer}}","aud":"{{McpAuthSettings.DefaultAudience}}","sub":"x","exp":{{exp}}}""");
        return $"{header}.{payload}.";
    }
}

/// <summary>Fail closed: over HTTP, without a usable key the server does not start at all.</summary>
public sealed class McpHttpStartupTests
{
    [Theory]
    [InlineData(null, "exige Mcp:Auth:SigningKeyFile")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==", "tem 128 bits")]
    [InlineData("isto não é base64", "não está em base64")]
    public async Task Http_mode_refuses_to_start_without_a_usable_key(string? keyContent, string expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Directory.CreateTempSubdirectory("mcp-jwt-");
        try
        {
            string[] args = ["--http", "--urls", $"http://127.0.0.1:{McpHttpServerFixture.FreePort()}"];
            if (keyContent is not null)
            {
                var keyFile = Path.Combine(dir.FullName, "mcp.key");
                await File.WriteAllTextAsync(keyFile, keyContent, ct);
                args = [.. args, "--Mcp:Auth:SigningKeyFile", keyFile];
            }

            using var process = Process.Start(McpHttpServerFixture.StartInfo(args))!;
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }

            Assert.Equal(1, process.ExitCode);
            Assert.Contains(expected, await stderr);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
