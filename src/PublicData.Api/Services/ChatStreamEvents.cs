using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PublicData.Api.Services;

/// <summary>
/// Server-Sent Events of POST /api/chat, in order: conversation, then tool_call/tool_result for each MCP call
/// (in real time, while the model works), then answer (or error), then done.
/// </summary>
public static class ChatStreamEvent
{
    public const string Conversation = "conversation";
    public const string ToolCall = "tool_call";
    public const string ToolResult = "tool_result";
    public const string Answer = "answer";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Done = "done";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record ConversationEvent(Guid ConversationId, string Title);

public sealed record ToolCallEvent(string CallId, string Tool, JsonElement Arguments, DateTimeOffset At);

public sealed record ToolResultEvent(string CallId, string Tool, bool IsError, long ElapsedMs, string? Preview, JsonElement? Result);

public sealed record AnswerEvent(long MessageId, string Text, string Model, long ElapsedMs, string? Source, bool UsedTools);

public sealed record MessageEvent(string Message);
