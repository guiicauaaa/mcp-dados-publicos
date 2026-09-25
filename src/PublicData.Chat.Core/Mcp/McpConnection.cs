using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PublicData.Chat.Core.Mcp;

/// <summary>
/// One long-lived MCP session (not one process per question): the server keeps its cache and the handshake
/// is paid once. Uses the official SDK client, so the protocol negotiation, request ids and the tools/list
/// are handled by the library, not by hand-written JSON-RPC.
/// </summary>
public sealed class McpConnection : IAsyncDisposable
{
    public const string ClientName = "public-data-chat";
    public const string ClientVersion = "1.0.0";

    private McpConnection(McpClient client, IReadOnlyList<McpClientTool> tools, string transport, string endpoint, TimeSpan connectTime)
    {
        Client = client;
        Tools = tools;
        Transport = transport;
        Endpoint = endpoint;
        ConnectTime = connectTime;
    }

    public McpClient Client { get; }

    /// <summary>Tools discovered via tools/list. McpClientTool is an AIFunction: it plugs straight into ChatOptions.Tools.</summary>
    public IReadOnlyList<McpClientTool> Tools { get; }

    public string Transport { get; }

    public string Endpoint { get; }

    public TimeSpan ConnectTime { get; }

    public string ServerName => Client.ServerInfo.Name;

    public string ServerVersion => Client.ServerInfo.Version;

    public string? ProtocolVersion => Client.NegotiatedProtocolVersion;

    public static async Task<McpConnection> ConnectAsync(
        McpSettings settings,
        Action<string>? onServerStderr = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        IClientTransport transport;
        string endpoint;

        if (settings.UseHttp)
        {
            var url = settings.HttpUrl ?? throw new McpStartupException("Mcp:Transport=http exige Mcp:HttpUrl (ex.: http://localhost:5100/mcp).");
            endpoint = url.ToString();
            transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "public-data-mcp",
                Endpoint = url,
                TransportMode = HttpTransportMode.StreamableHttp,
            }, loggerFactory);
        }
        else
        {
            var serverDll = settings.ServerDll ?? McpServerLocator.DefaultServerDll;
            if (!File.Exists(serverDll))
            {
                throw new McpStartupException($"Servidor MCP não encontrado em {serverDll}. Rode 'dotnet build' na raiz do repositório.");
            }

            endpoint = serverDll;
            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "public-data-mcp",
                // Never 'dotnet run' here: build errors go to stdout and vanish, and the slow start
                // makes the client fall back to the legacy handshake.
                Command = McpServerLocator.ResolveDotnetHost(),
                Arguments = [serverDll],
                WorkingDirectory = Path.GetDirectoryName(serverDll),
                // Dispose always waits this long before killing the child (measured: 5.1 s by default, 1.1 s here).
                ShutdownTimeout = TimeSpan.FromSeconds(1),
                StandardErrorLines = onServerStderr,
            }, loggerFactory);
        }

        var stopwatch = Stopwatch.StartNew();
        McpClient client;
        try
        {
            client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ClientInfo = new Implementation { Name = ClientName, Version = ClientVersion },
                // With the 5 s default, a cold start (antivirus scan, first run) falls back to protocol 2025-11-25.
                DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
                InitializationTimeout = TimeSpan.FromSeconds(60),
            }, loggerFactory, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new McpStartupException($"O servidor MCP não iniciou ({endpoint}): {ex.Message}", ex);
        }

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return new McpConnection(client, [.. tools], settings.UseHttp ? "http" : "stdio", endpoint, stopwatch.Elapsed);
    }

    public ValueTask DisposeAsync() => Client.DisposeAsync();
}

public sealed class McpStartupException(string message, Exception? inner = null) : Exception(message, inner);

public static class McpServerLocator
{
    /// <summary>build/CopyMcpServer.targets puts the server here after every build of the host.</summary>
    public static string DefaultServerDll => Path.Combine(AppContext.BaseDirectory, "mcp-server", "PublicData.McpServer.dll");

    /// <summary>
    /// The same dotnet host that runs this process: &lt;root&gt;/shared/Microsoft.NETCore.App/&lt;version&gt;/ leads to
    /// &lt;root&gt;/dotnet(.exe). Works even when the SDK is not on PATH or another dotnet comes first.
    /// </summary>
    public static string ResolveDotnetHost()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(host) ? host : "dotnet";
    }
}
