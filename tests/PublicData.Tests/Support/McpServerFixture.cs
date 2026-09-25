using System.Collections.Concurrent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using PublicData.Chat.Core.Mcp;

namespace PublicData.Tests.Support;

/// <summary>
/// The REAL MCP server as a child process over stdio, one per test class. The cases used by the tests fail
/// validation before any HTTP call, so no test needs the internet.
/// </summary>
public sealed class McpServerFixture : IAsyncLifetime
{
    public ConcurrentQueue<string> StderrLines { get; } = new();

    public McpClient Client { get; private set; } = null!;

    public IReadOnlyList<McpClientTool> Tools { get; private set; } = [];

    public static string ServerDll => Path.Combine(AppContext.BaseDirectory, "PublicData.McpServer.dll");

    public async ValueTask InitializeAsync()
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "public-data-mcp-tests",
            Command = McpServerLocator.ResolveDotnetHost(),
            Arguments = [ServerDll],
            WorkingDirectory = AppContext.BaseDirectory,
            ShutdownTimeout = TimeSpan.FromSeconds(1),
            StandardErrorLines = StderrLines.Enqueue,
        });

        Client = await McpClient.CreateAsync(transport, new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "tests", Version = "1.0.0" },
            DiscoverProbeTimeout = TimeSpan.FromSeconds(30),
            InitializationTimeout = TimeSpan.FromSeconds(60),
        });
        Tools = [.. await Client.ListToolsAsync()];
    }

    public async ValueTask DisposeAsync() => await Client.DisposeAsync();
}
