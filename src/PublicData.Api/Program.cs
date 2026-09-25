using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PublicData.Api.Data;
using PublicData.Api.Endpoints;
using PublicData.Api.Services;
using PublicData.Chat.Core;
using Scalar.AspNetCore;

if (args.Contains("--healthcheck"))
{
    // Docker HEALTHCHECK without curl (the .NET runtime images do not ship it).
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection(ChatSettings.SectionName));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ChatSettings>>().Value);
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<AppDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:Default (PostgreSQL)."))
    .UseSnakeCaseNamingConvention());

// Ollama and MCP are long-lived singletons that connect in the background and retry on their own,
// so the API (and the Angular UI) come up even while the model loads or the MCP service starts.
// Resolved through the interfaces, so a test double registered in their place starts no background work.
builder.Services.AddSingleton<IModelGateway, OllamaGateway>();
builder.Services.AddSingleton<IMcpGateway, McpGateway>();
builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IModelGateway>() as IHostedService ?? NoopHostedService.Instance);
builder.Services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IMcpGateway>() as IHostedService ?? NoopHostedService.Instance);

builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ToolInvoker>();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Configuration.GetValue("Database:ApplyMigrationsOnStartup", true) && !await MigrateAsync(app))
{
    return 1;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference("/docs", options => options.WithTitle("Assistente de Dados Públicos - API"));

// In Docker the Angular build is served from wwwroot, same origin: no CORS.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapStatusEndpoints();
app.MapChatEndpoints();
app.MapConversationEndpoints();
app.MapAuditEndpoints();
app.MapFallback("/api/{**path}", () => TypedResults.NotFound()); // unknown API routes are 404, not the SPA page
app.MapFallbackToFile("index.html");

await app.RunAsync();
return 0;

// In Docker Compose PostgreSQL may still be starting: retry for a short while, then stop with a clear message.
static async Task<bool> MigrateAsync(WebApplication app)
{
    const int attempts = 10;
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PublicData.Api.Database");
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            logger.LogInformation("Banco de dados atualizado (migrations aplicadas).");
            return true;
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException)
        {
            if (attempt == attempts)
            {
                logger.LogError(
                    "PostgreSQL indisponível ({Message}). Suba o PostgreSQL ou ajuste ConnectionStrings__Default; " +
                    "sem banco, use a Opção A do README (Docker Compose).", ex.Message);
                return false;
            }

            logger.LogWarning("PostgreSQL indisponível ({Message}); nova tentativa em 3 s ({Attempt}/{Attempts}).", ex.Message, attempt, attempts);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

public partial class Program;
