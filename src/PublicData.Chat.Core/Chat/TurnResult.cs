using PublicData.Chat.Core.Llm;

namespace PublicData.Chat.Core.Chat;

/// <summary>A source cited in a tool result ("source" and "queried_at" fields).</summary>
public sealed record ToolSource(string Source, string? QueriedAt)
{
    public override string ToString() => QueriedAt is null ? Source : $"{Source} (consultado via MCP em {QueriedAt})";
}

public sealed record TurnResult(
    string Answer,
    IReadOnlyList<ToolSource> Sources,
    IReadOnlyList<ToolTraceEvent> ToolEvents,
    TimeSpan Elapsed,
    string Model,
    bool RetriedWithoutTools)
{
    /// <summary>Built by the client from the MCP result, never by the model, so it is always accurate.</summary>
    public string? SourceLine => Sources.Count == 0 ? null : "Fonte: " + string.Join("; ", Sources);

    public bool UsedTools => ToolEvents.Any(e => e.Kind == ToolTraceKind.Call);
}
