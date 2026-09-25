using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Mcp;
using PublicData.McpServer.Security;
using PublicData.Tests.Support;

namespace PublicData.Tests.Chat;

public sealed class McpTokenIssuerTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(48);

    [Fact]
    public async Task Token_signed_by_the_client_passes_the_server_validation()
    {
        var token = new McpTokenIssuer(new McpAuthSettings(), Key).GetToken();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token,
            McpAuthOptions.CreateValidationParameters(new McpAuthOptions(), Key));

        Assert.True(result.IsValid, result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(McpAuthOptions.DefaultIssuer, jwt.Issuer);
        Assert.Equal(McpAuthOptions.DefaultAudience, Assert.Single(jwt.Audiences));
        Assert.Equal(McpConnection.ClientName, jwt.Subject);
        Assert.Equal(TimeSpan.FromMinutes(5), jwt.ValidTo - jwt.ValidFrom);
    }

    [Fact]
    public void Client_and_server_defaults_match()
    {
        Assert.Equal(McpAuthOptions.DefaultIssuer, McpAuthSettings.DefaultIssuer);
        Assert.Equal(McpAuthOptions.DefaultAudience, McpAuthSettings.DefaultAudience);
    }

    [Fact]
    public void Reuses_the_token_and_signs_a_new_one_a_minute_before_expiry()
    {
        var clock = new ManualTimeProvider(FixedTimeProvider.Sept25.GetUtcNow());
        var issuer = new McpTokenIssuer(new McpAuthSettings(), Key, clock);

        var first = issuer.GetToken();
        clock.Advance(TimeSpan.FromMinutes(3.5));
        Assert.Same(first, issuer.GetToken());

        clock.Advance(TimeSpan.FromMinutes(0.6));
        Assert.NotEqual(first, issuer.GetToken());
    }

    [Theory]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==", "tem 128 bits")]
    [InlineData("não é base64", "não está em base64")]
    public void Unusable_key_file_is_rejected(string content, string expected)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, content);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                McpTokenIssuer.FromKeyFile(new McpAuthSettings { SigningKeyFile = file }));

            Assert.Contains(expected, ex.Message);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Missing_key_file_fails_the_connection_before_any_request()
    {
        var settings = new McpSettings
        {
            Transport = "http",
            HttpUrl = new Uri("http://127.0.0.1:9/mcp"),
            Auth = new McpAuthSettings { SigningKeyFile = Path.Combine(Path.GetTempPath(), "nao-existe", "mcp.key") },
        };

        var ex = await Assert.ThrowsAsync<McpStartupException>(() =>
            McpConnection.ConnectAsync(settings, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Chave de assinatura do MCP não encontrada", ex.Message);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
