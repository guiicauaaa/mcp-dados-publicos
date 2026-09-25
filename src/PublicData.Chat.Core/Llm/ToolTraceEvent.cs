namespace PublicData.Chat.Core.Llm;

public static class ToolTraceKind
{
    public const string Call = "call";
    public const string Result = "result";
    public const string Error = "error";
}

/// <summary>One step of a tool call, for the console, the web UI (SSE) and the audit trail.</summary>
public sealed record ToolTraceEvent(
    string Kind,
    string CallId,
    string ToolName,
    string ArgumentsJson,
    string? ResultJson,
    string? ResultPreview,
    long? ElapsedMs,
    DateTimeOffset Timestamp);
