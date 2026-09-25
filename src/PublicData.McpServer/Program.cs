using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using PublicData.McpServer.Hosting;

namespace PublicData.McpServer;

/// <summary>
/// MCP server entry point. Two transports, same tools:
/// <list type="bullet">
///   <item>stdio (default): started as a child process by the chat, like the reference repository;</item>
///   <item>Streamable HTTP (<c>--http</c> or <c>MCP_TRANSPORT=http</c>): a standalone service, used by Docker Compose.</item>
/// </list>
/// </summary>
public static class Program
{
    public static Task Main(string[] args)
    {
        var useHttp = args.Contains("--http")
            || string.Equals(Environment.GetEnvironmentVariable("MCP_TRANSPORT"), "http", StringComparison.OrdinalIgnoreCase);

        // The command-line configuration provider would read "--http --urls x" as http="--urls" and lose the URL.
        var hostArgs = args.Where(a => a != "--http").ToArray();
        return useHttp ? RunHttpAsync(hostArgs) : RunStdioAsync(hostArgs);
    }

    private static async Task RunStdioAsync(string[] args)
    {
        // stderr carries the logs to the client, which reads them as UTF-8; without this the Windows
        // console code page mangles accents. stdout (JSON-RPC) is unaffected: the SDK writes to the raw stream.
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        // DisableDefaults: the server runs from the host's folder tree, so it must not pick up
        // an appsettings.json that belongs to the chat or the API. Only env vars and args apply.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });
        builder.Configuration.AddEnvironmentVariables("PUBLICDATA_");
        builder.Configuration.AddCommandLine(args);

        // stdout is the JSON-RPC channel: every log line must go to stderr, one line per entry.
        // Never use Console.Write* in this project: a write without a newline hangs the client silently.
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.IncludeScopes = false;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        ConfigureLogLevels(builder.Logging);

        builder.Services.AddPublicDataServices();
        builder.Services
            .AddMcpServer(McpServerSetup.Configure)
            .WithStdioServerTransport()
            .WithPublicDataTools();

        await builder.Build().RunAsync();
    }

    private static async Task RunHttpAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.IncludeScopes = false;
            o.TimestampFormat = "HH:mm:ss ";
        });
        ConfigureLogLevels(builder.Logging);

        builder.Services.AddPublicDataServices();
        builder.Services
            .AddMcpServer(McpServerSetup.Configure)
            // Stateless: every request is independent, so the service scales and restarts without sessions.
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithPublicDataTools();

        var app = builder.Build();
        app.MapMcp("/mcp");
        app.MapGet("/health", () => Results.Ok(new { status = "ok", server = McpServerSetup.Name, version = McpServerSetup.Version }));
        await app.RunAsync();
    }

    private static void ConfigureLogLevels(ILoggingBuilder logging)
    {
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddFilter("PublicData", LogLevel.Information);
        logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
    }
}
