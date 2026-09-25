namespace PublicData.Api.Data;

public sealed class Conversation
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];
}

public static class MessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public sealed class ConversationMessage
{
    public long Id { get; set; }

    public Guid ConversationId { get; set; }

    public Conversation? Conversation { get; set; }

    public required string Role { get; set; }

    public required string Content { get; set; }

    /// <summary>Model that wrote the answer (assistant messages only).</summary>
    public string? Model { get; set; }

    public int? ElapsedMs { get; set; }

    /// <summary>"Fonte: …" built from the MCP result, never by the model.</summary>
    public string? SourceLine { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<ToolCallAudit> ToolCalls { get; set; } = [];
}

public static class ToolCallOrigins
{
    /// <summary>Called by the model during a chat turn.</summary>
    public const string Chat = "chat";

    /// <summary>Called directly from the tools panel, without the model.</summary>
    public const string Manual = "manual";
}

/// <summary>
/// Audit trail: every MCP tool call, from the chat or from the tools panel, with arguments, result,
/// duration and which server answered. Public data only; the tools never return personal or bank data.
/// </summary>
public sealed class ToolCallAudit
{
    public long Id { get; set; }

    public Guid? ConversationId { get; set; }

    public long? MessageId { get; set; }

    public ConversationMessage? Message { get; set; }

    public required string Origin { get; set; }

    public required string CallId { get; set; }

    public required string ToolName { get; set; }

    /// <summary>jsonb.</summary>
    public required string ArgumentsJson { get; set; }

    /// <summary>jsonb: the compact result handed to the model, or {"error": "..."}.</summary>
    public string? ResultJson { get; set; }

    public bool IsError { get; set; }

    public string? ErrorMessage { get; set; }

    public int DurationMs { get; set; }

    /// <summary>"public-data-mcp 1.0.0".</summary>
    public required string McpServer { get; set; }

    /// <summary>"stdio" or "http".</summary>
    public required string McpTransport { get; set; }

    public string? Model { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
