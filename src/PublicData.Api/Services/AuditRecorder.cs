using PublicData.Api.Data;
using PublicData.Chat.Core.Llm;

namespace PublicData.Api.Services;

/// <summary>Turns the tracer events of a turn (call, then result or error) into audit rows.</summary>
public static class AuditRecorder
{
    public static List<ToolCallAudit> FromEvents(
        IReadOnlyList<ToolTraceEvent> events,
        string origin,
        string mcpServer,
        string mcpTransport,
        string? model,
        Guid? conversationId)
    {
        var rows = new List<ToolCallAudit>();
        foreach (var call in events.Where(e => e.Kind == ToolTraceKind.Call))
        {
            var outcome = events.FirstOrDefault(e => e.CallId == call.CallId && e.Kind != ToolTraceKind.Call);
            var isError = outcome?.Kind == ToolTraceKind.Error;
            rows.Add(new ToolCallAudit
            {
                Origin = origin,
                ConversationId = conversationId,
                CallId = call.CallId,
                ToolName = call.ToolName,
                ArgumentsJson = string.IsNullOrWhiteSpace(call.ArgumentsJson) || call.ArgumentsJson == "null" ? "{}" : call.ArgumentsJson,
                ResultJson = outcome?.ResultJson,
                IsError = outcome is null || isError,
                ErrorMessage = outcome is null ? "A chamada não terminou (turno cancelado)." : isError ? outcome.ResultPreview : null,
                DurationMs = (int)(outcome?.ElapsedMs ?? 0),
                McpServer = mcpServer,
                McpTransport = mcpTransport,
                Model = model,
                CreatedAt = call.Timestamp.ToUniversalTime(),
            });
        }

        return rows;
    }
}
