using Microsoft.Extensions.AI;

namespace PublicData.Tests.Support;

/// <summary>
/// A fake model: returns scripted answers in order and records what each request carried. It goes inside the
/// same pipeline used in production (FunctionInvoking, tracer, repair), so the tests exercise the real loop.
/// </summary>
public sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>[] script) : IChatClient
{
    private int _next;

    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    public static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    public static ChatResponse Call(string tool, Dictionary<string, object?> arguments, string callId = "call_1") =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, tool, arguments)]));

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Requests.Add((list, options?.Clone()));
        var step = _next < script.Length ? script[_next] : script[^1];
        _next++;
        return Task.FromResult(step(list, options));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The chat engine uses non-streaming responses.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
