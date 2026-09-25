using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PublicData.Api.Data;
using PublicData.Api.Services;

namespace PublicData.Tests.Support;

/// <summary>
/// The whole API in memory with a real PostgreSQL (throwaway database per run), the real MCP server over stdio
/// and a scripted model instead of Ollama. PUBLICDATA_TEST_DB holds the server connection string
/// (the CI sets it for its PostgreSQL service); without it the API tests are skipped.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ConnectionVariable = "PUBLICDATA_TEST_DB";

    private readonly string? _connectionString = BuildConnectionString();

    public FakeModelGateway Model { get; } = new();

    public static string? SkipReason => Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 }
        ? null
        : $"Defina {ConnectionVariable} (ex.: Host=localhost;Username=postgres;Password=postgres) para rodar os testes da API com PostgreSQL.";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString ?? "Host=invalid");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Chat:Mcp:Transport", "stdio");
        builder.UseSetting("Chat:Mcp:ServerDll", McpServerFixture.ServerDll);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IModelGateway>(Model);
            services.AddSingleton<TimeProvider>(FixedTimeProvider.Sept25);
        });
    }

    public async ValueTask InitializeAsync()
    {
        if (_connectionString is null)
        {
            return;
        }

        // Creating the client starts the host, which applies the migrations to the new database.
        _ = CreateClient();
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.CanConnectAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_connectionString is not null)
        {
            await using (var scope = Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
        }

        await base.DisposeAsync();
    }

    private static string? BuildConnectionString()
    {
        var baseConnection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            return null;
        }

        return new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = "dados_publicos_tests_" + Guid.NewGuid().ToString("N")[..8],
        }.ConnectionString;
    }
}

/// <summary>Always ready; the test decides what the "model" answers.</summary>
public sealed class FakeModelGateway : IModelGateway
{
    public ScriptedChatClient Script { get; set; } = new((_, _) => ScriptedChatClient.Text("ok"));

    public ModelStatus Status => new(ComponentState.Ready, "http://fake", "fake-model", "fake-model", "0.0.0", false, null);

    public IChatClient? Client => Script;

    public string? Model => "fake-model";

    public Task<bool> EnsureReadyAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public List<string> Failures { get; } = [];

    public void ReportFailure(string message) => Failures.Add(message);
}
