using Microsoft.EntityFrameworkCore;
using PublicData.Api.Data;
using PublicData.Api.Services;

namespace PublicData.Api.Endpoints;

public sealed record DatabaseStatus(string State, string? Message);

public sealed record SystemStatus(ModelStatus Ollama, McpStatus Mcp, DatabaseStatus Database);

public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }))
            .WithTags("Status")
            .WithSummary("Liveness: a API está no ar.");

        app.MapGet("/api/status", async (IModelGateway model, IMcpGateway mcp, AppDbContext db, CancellationToken ct) =>
            {
                DatabaseStatus database;
                try
                {
                    database = await db.Database.CanConnectAsync(ct)
                        ? new DatabaseStatus(ComponentState.Ready, null)
                        : new DatabaseStatus(ComponentState.Unavailable, "Não consegui conectar ao PostgreSQL.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    database = new DatabaseStatus(ComponentState.Unavailable, ex.Message);
                }

                return new SystemStatus(model.Status, mcp.Status, database);
            })
            .WithTags("Status")
            .WithSummary("Estado do Ollama (modelo), do servidor MCP (ferramentas do tools/list) e do banco.");

        var mcpGroup = app.MapGroup("/api/mcp").WithTags("MCP");

        mcpGroup.MapGet("/log", (IMcpGateway mcp, long? since) => mcp.LogSince(since ?? 0))
            .WithSummary("Linhas do stderr do processo servidor MCP (modo stdio).");

        mcpGroup.MapPost("/reconnect", async (IMcpGateway mcp, CancellationToken ct) =>
            {
                await mcp.ReconnectAsync(ct);
                return mcp.Status;
            })
            .WithSummary("Reconecta ao servidor MCP (útil se o processo servidor caiu).");

        mcpGroup.MapPost("/tools/{name}/invoke", async Task<IResult> (string name, ToolInvokeRequest request, ToolInvoker invoker, CancellationToken ct) =>
            {
                try
                {
                    var response = await invoker.InvokeAsync(name, request, ct);
                    return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
                }
                catch (InvalidOperationException ex)
                {
                    return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithSummary("Chama uma ferramenta MCP diretamente, sem o modelo (também auditado, origem 'manual').");
    }
}
