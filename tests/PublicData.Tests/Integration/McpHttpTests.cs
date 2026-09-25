using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Protocol;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Mcp;
using PublicData.Tests.Support;

namespace PublicData.Tests.Integration;

/// <summary>
/// The same server in Streamable HTTP mode (the Docker Compose setup), as a real process on a free port,
/// reached through the same McpConnection the chat and the API use.
/// </summary>
public sealed class McpHttpTests : IAsyncLifetime
{
    private Process? _server;
    private Uri _endpoint = null!;

    public async ValueTask InitializeAsync()
    {
        var port = FreePort();
        _endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
        _server = Process.Start(new ProcessStartInfo(McpServerLocator.ResolveDotnetHost())
        {
            ArgumentList = { McpServerFixture.ServerDll, "--http", "--urls", $"http://127.0.0.1:{port}" },
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var health = new Uri($"http://127.0.0.1:{port}/health");
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !_server.HasExited)
        {
            try
            {
                if ((await http.GetAsync(health)).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }

            await Task.Delay(200);
        }

        // xunit does not call DisposeAsync when InitializeAsync throws: never leave the process behind.
        await DisposeAsync();
        throw new TimeoutException("O servidor MCP em modo HTTP não respondeu em /health.");
    }

    public ValueTask DisposeAsync()
    {
        if (_server is { HasExited: false })
        {
            _server.Kill(entireProcessTree: true);
        }

        _server?.Dispose();
        _server = null;
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Connects_lists_tools_and_calls_one_over_http()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var connection = await McpConnection.ConnectAsync(new McpSettings { Transport = "http", HttpUrl = _endpoint }, cancellationToken: ct);

        Assert.Equal("http", connection.Transport);
        Assert.Equal("public-data-mcp", connection.ServerName);
        Assert.Contains(connection.Tools, t => t.Name == "get_city_amendments");

        var result = await connection.Client.CallToolAsync("get_city_amendments",
            new Dictionary<string, object?> { ["city"] = "Campinas", ["state"] = "XX" }, cancellationToken: ct);

        Assert.True(result.IsError);
        Assert.Contains("UF 'XX' inválida", result.Content.OfType<TextContentBlock>().Single().Text);
    }

    [Fact]
    public async Task Browser_origin_is_refused_to_prevent_dns_rebinding()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", "http://evil.example");
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
