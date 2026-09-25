using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PublicData.Api.Data;
using PublicData.Api.Services;

namespace PublicData.Api.Endpoints;

public sealed record ConversationSummaryDto(Guid Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int MessageCount);

public sealed record ToolCallDto(long Id, string CallId, string ToolName, JsonElement? Arguments, bool IsError, string? ErrorMessage, int DurationMs, JsonElement? Result);

public sealed record MessageDto(long Id, string Role, string Content, string? Model, int? ElapsedMs, string? SourceLine, DateTimeOffset CreatedAt, IReadOnlyList<ToolCallDto> ToolCalls);

public sealed record ConversationDto(Guid Id, string Title, DateTimeOffset CreatedAt, IReadOnlyList<MessageDto> Messages);

public static class ConversationEndpoints
{
    public static void MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversations").WithTags("Conversas");

        group.MapGet("/", async (AppDbContext db, int? take, CancellationToken ct) =>
                await db.Conversations
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(Math.Clamp(take ?? 50, 1, 200))
                    .Select(c => new ConversationSummaryDto(c.Id, c.Title, c.CreatedAt, c.UpdatedAt, c.Messages.Count))
                    .ToListAsync(ct))
            .WithSummary("Conversas mais recentes.");

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, AppDbContext db, CancellationToken ct) =>
            {
                var conversation = await db.Conversations.AsNoTracking()
                    .Include(c => c.Messages).ThenInclude(m => m.ToolCalls)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(c => c.Id == id, ct);
                if (conversation is null)
                {
                    return TypedResults.NotFound();
                }

                var messages = conversation.Messages
                    .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                    .Select(m => new MessageDto(m.Id, m.Role, m.Content, m.Model, m.ElapsedMs, m.SourceLine, m.CreatedAt,
                        [.. m.ToolCalls.OrderBy(t => t.CreatedAt).Select(t => new ToolCallDto(t.Id, t.CallId, t.ToolName,
                            ChatService.ParseJson(t.ArgumentsJson), t.IsError, t.ErrorMessage, t.DurationMs, ChatService.ParseJson(t.ResultJson)))]))
                    .ToList();
                return TypedResults.Ok(new ConversationDto(conversation.Id, conversation.Title, conversation.CreatedAt, messages));
            })
            .WithSummary("Mensagens de uma conversa, com as chamadas MCP de cada resposta.");

        group.MapDelete("/{id:guid}", async Task<IResult> (Guid id, AppDbContext db, CancellationToken ct) =>
            {
                var deleted = await db.Conversations.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
                return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
            })
            .WithSummary("Apaga a conversa. A trilha de auditoria das chamadas MCP é mantida.");
    }
}
