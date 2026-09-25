using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using PublicData.McpServer.Hosting;
using PublicData.McpServer.Security;
using PublicData.Shared;

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
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--healthcheck"))
        {
            return await HealthProbeAsync();
        }

        var useHttp = args.Contains("--http")
            || string.Equals(Environment.GetEnvironmentVariable("MCP_TRANSPORT"), "http", StringComparison.OrdinalIgnoreCase);

        // The command-line configuration provider would read "--http --urls x" as http="--urls" and lose the URL.
        var hostArgs = args.Where(a => a != "--http").ToArray();
        if (useHttp)
        {
            return await RunHttpAsync(hostArgs);
        }

        await RunStdioAsync(hostArgs);
        return 0;
    }

    /// <summary>Docker HEALTHCHECK without curl (the .NET runtime images do not ship it).</summary>
    private static async Task<int> HealthProbeAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', ',')[0] ?? "8080";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
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

    private static async Task<int> RunHttpAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.IncludeScopes = false;
            o.TimestampFormat = "HH:mm:ss ";
        });
        ConfigureLogLevels(builder.Logging);
        // Rejected tokens (the reason and the "challenged" line) are Information; accepted ones are Debug.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Information);

        // Fail closed: over HTTP, /mcp requires a JWT signed with the key shared with the API. Without a key the
        // server does not start, unless anonymous access was asked for explicitly (local development).
        var auth = builder.Configuration.GetSection(McpAuthOptions.SectionName).Get<McpAuthOptions>() ?? new McpAuthOptions();
        if (!auth.AllowAnonymous)
        {
            byte[] key;
            try
            {
                key = SigningKeyFile.Read(auth.SigningKeyFile is { Length: > 0 } path
                    ? path
                    : throw new InvalidOperationException(
                        "O modo HTTP exige Mcp:Auth:SigningKeyFile (chave compartilhada com a API). " +
                        "Só para desenvolvimento local: --Mcp:Auth:AllowAnonymous true."));
            }
            catch (InvalidOperationException ex)
            {
                await Console.Error.WriteLineAsync($"Servidor MCP não iniciado: {ex.Message}");
                return 1;
            }

            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(o =>
                {
                    o.MapInboundClaims = false;
                    o.TokenValidationParameters = McpAuthOptions.CreateValidationParameters(auth, key);
                });
            builder.Services.AddAuthorization();
        }

        builder.Services.AddPublicDataServices();
        builder.Services
            .AddMcpServer(McpServerSetup.Configure)
            // Stateless: every request is independent, so the service scales and restarts without sessions.
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
            .WithPublicDataTools();

        var app = builder.Build();

        // The MCP spec asks Streamable HTTP servers to validate Origin (DNS rebinding). The only client is the
        // API, a server-side .NET client that sends no Origin; browser requests are refused unless allowed.
        var allowedOrigins = app.Configuration.GetSection("Mcp:AllowedOrigins").Get<string[]>() ?? [];
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (origin.Length > 0 && context.Request.Path.StartsWithSegments("/mcp")
                && !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });

        var mcp = app.MapMcp("/mcp");
        if (auth.AllowAnonymous)
        {
            app.Logger.LogWarning("Mcp:Auth:AllowAnonymous ligado: /mcp aceita requisições sem token. Use só em desenvolvimento local.");
        }
        else
        {
            app.UseAuthentication();
            app.UseAuthorization();
            mcp.RequireAuthorization();
        }

        // Stays anonymous: Docker HEALTHCHECK and the wait loops only need to know the process is up.
        app.MapGet("/health", () => Results.Ok(new { status = "ok", server = McpServerSetup.Name, version = McpServerSetup.Version }));
        await app.RunAsync();
        return 0;
    }

    private static void ConfigureLogLevels(ILoggingBuilder logging)
    {
        logging.SetMinimumLevel(LogLevel.Warning);
        logging.AddFilter("PublicData", LogLevel.Information);
        logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
    }
}
