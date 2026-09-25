using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using PublicData.Chat.Core.Mcp;

namespace PublicData.Tests.Support;

/// <summary>
/// The server in Streamable HTTP mode, as in Docker Compose: a real process on a free port that requires a JWT
/// signed with a throwaway key. One process per test class.
/// </summary>
public sealed class McpHttpServerFixture : IAsyncLifetime
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("mcp-jwt-");
    private Process? _server;

    /// <summary>384 random bits, like the key the Compose init container generates.</summary>
    public byte[] Key { get; } = RandomNumberGenerator.GetBytes(48);

    public string KeyFile => Path.Combine(_dir.FullName, "mcp.key");

    public Uri Endpoint { get; private set; } = null!;

    public Uri Health { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(KeyFile, Convert.ToBase64String(Key));

        var port = FreePort();
        Endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
        Health = new Uri($"http://127.0.0.1:{port}/health");
        _server = Process.Start(StartInfo("--http", "--urls", $"http://127.0.0.1:{port}", "--Mcp:Auth:SigningKeyFile", KeyFile))!;
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !_server.HasExited)
        {
            try
            {
                if ((await http.GetAsync(Health)).IsSuccessStatusCode)
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
        if (Directory.Exists(_dir.FullName))
        {
            _dir.Delete(recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    public static ProcessStartInfo StartInfo(params string[] args)
    {
        var info = new ProcessStartInfo(McpServerLocator.ResolveDotnetHost())
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(McpServerFixture.ServerDll);
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        return info;
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
