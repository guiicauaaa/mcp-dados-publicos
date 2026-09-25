using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PublicData.Api.Data;
using PublicData.Api.Services;

namespace PublicData.Api.Endpoints;

public sealed record AuditItemDto(
    long Id,
    DateTimeOffset CreatedAt,
    string Origin,
    string ToolName,
    JsonElement? Arguments,
    bool IsError,
    string? ErrorMessage,
    int DurationMs,
    string McpServer,
    string McpTransport,
    string? Model,
    Guid? ConversationId,
    string? ConversationTitle);

public sealed record AuditDetailDto(AuditItemDto Call, JsonElement? Result);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ToolUsageDto(string ToolName, int Total, int Errors, double AverageDurationMs);

public sealed record AuditSummaryDto(int Total, int Errors, double AverageDurationMs, int Last24Hours, IReadOnlyList<ToolUsageDto> ByTool);

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/audit").WithTags("Auditoria");

        group.MapGet("/tool-calls", async (AppDbContext db, string? tool, string? status, string? origin, int? page, int? pageSize, CancellationToken ct) =>
            {
                var size = Math.Clamp(pageSize ?? 20, 1, 100);
                var current = Math.Max(page ?? 1, 1);
                var query = Filter(db.ToolCalls.AsNoTracking(), tool, status, origin);

                var total = await query.CountAsync(ct);
                var rows = await query
                    .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                    .Skip((current - 1) * size).Take(size)
                    .Select(t => new
                    {
                        t.Id, t.CreatedAt, t.Origin, t.ToolName, t.ArgumentsJson, t.IsError, t.ErrorMessage, t.DurationMs,
                        t.McpServer, t.McpTransport, t.Model, t.ConversationId,
                        Title = db.Conversations.Where(c => c.Id == t.ConversationId).Select(c => c.Title).FirstOrDefault(),
                    })
                    .ToListAsync(ct);

                var items = rows.Select(r => new AuditItemDto(r.Id, r.CreatedAt, r.Origin, r.ToolName, ChatService.ParseJson(r.ArgumentsJson),
                    r.IsError, r.ErrorMessage, r.DurationMs, r.McpServer, r.McpTransport, r.Model, r.ConversationId, r.Title)).ToList();
                return new PagedResult<AuditItemDto>(items, total, current, size);
            })
            .WithSummary("Trilha de auditoria das chamadas MCP (filtros: tool, status=ok|error, origin=chat|manual).");

        group.MapGet("/tool-calls/{id:long}", async Task<IResult> (long id, AppDbContext db, CancellationToken ct) =>
            {
                var t = await db.ToolCalls.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
                if (t is null)
                {
                    return TypedResults.NotFound();
                }

                var title = await db.Conversations.Where(c => c.Id == t.ConversationId).Select(c => c.Title).FirstOrDefaultAsync(ct);
                var item = new AuditItemDto(t.Id, t.CreatedAt, t.Origin, t.ToolName, ChatService.ParseJson(t.ArgumentsJson), t.IsError,
                    t.ErrorMessage, t.DurationMs, t.McpServer, t.McpTransport, t.Model, t.ConversationId, title);
                return TypedResults.Ok(new AuditDetailDto(item, ChatService.ParseJson(t.ResultJson)));
            })
            .WithSummary("Uma chamada MCP com o resultado completo entregue ao modelo.");

        group.MapGet("/summary", async (AppDbContext db, TimeProvider clock, CancellationToken ct) =>
            {
                var since = clock.GetUtcNow().AddHours(-24);
                var byTool = await db.ToolCalls.AsNoTracking()
                    .GroupBy(t => t.ToolName)
                    .Select(g => new ToolUsageDto(g.Key, g.Count(), g.Count(t => t.IsError), g.Average(t => (double)t.DurationMs)))
                    .ToListAsync(ct);
                var total = byTool.Sum(t => t.Total);
                var average = total == 0 ? 0 : byTool.Sum(t => t.AverageDurationMs * t.Total) / total;
                var last24 = await db.ToolCalls.CountAsync(t => t.CreatedAt >= since, ct);
                return new AuditSummaryDto(total, byTool.Sum(t => t.Errors), Math.Round(average, 1), last24,
                    [.. byTool.OrderByDescending(t => t.Total)]);
            })
            .WithSummary("Totais da auditoria: chamadas, erros e tempo médio por ferramenta.");
    }

    private static IQueryable<ToolCallAudit> Filter(IQueryable<ToolCallAudit> query, string? tool, string? status, string? origin)
    {
        if (!string.IsNullOrWhiteSpace(tool))
        {
            query = query.Where(t => t.ToolName == tool);
        }

        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.IsError);
        }
        else if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => !t.IsError);
        }

        if (!string.IsNullOrWhiteSpace(origin))
        {
            query = query.Where(t => t.Origin == origin);
        }

        return query;
    }
}
