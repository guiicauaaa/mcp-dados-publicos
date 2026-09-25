using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PublicData.Chat.Core.Llm;

namespace PublicData.Chat.Core.Chat;

/// <summary>
/// Runs one chat turn. Stateless about the conversation: the caller owns the history (a list in the console,
/// the database in the API) and passes the text-only messages of previous turns.
/// </summary>
public sealed class ChatEngine(IChatClient ollama, ChatSettings settings, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public const string OllamaFailureAfterToolAnswer =
        "O modelo falhou ao redigir a resposta (HTTP 500 do Ollama). Tente de novo.";

    public async Task<TurnResult> AskAsync(
        IReadOnlyList<ChatMessage> history,
        string question,
        IReadOnlyList<AITool> tools,
        string model,
        Action<ToolTraceEvent>? onToolEvent = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<ToolTraceEvent>();
        void Publish(ToolTraceEvent e)
        {
            lock (events)
            {
                events.Add(e);
            }

            onToolEvent?.Invoke(e);
        }

        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turn.CancelAfter(settings.TurnTimeout);

        var pipeline = ChatPipeline.Build(ollama, Publish, settings.ToolTimeout, _clock);
        var options = ChatPipeline.CreateOptions(tools, settings, model);
        options.Instructions = SystemPrompt.Build(BrasiliaClock.Now(_clock), model);

        // Compact history: only the text of past turns. Measured: with the full history (old tool calls and
        // results), the follow-up "E em 2025?" failed 2 out of 2; with the compact one it passed 2 out of 2.
        List<ChatMessage> messages =
        [
            .. history
                .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) && !string.IsNullOrWhiteSpace(m.Text))
                .TakeLast(settings.HistoryMessages)
                .Select(m => new ChatMessage(m.Role, m.Text)),
            new ChatMessage(ChatRole.User, question),
        ];

        var stopwatch = Stopwatch.StartNew();
        var retried = false;
        ChatResponse response;
        try
        {
            response = await pipeline.GetResponseAsync(messages, options, turn.Token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.InternalServerError)
        {
            // Ollama aborts degenerate generations ("token repeat limit reached") with HTTP 500.
            bool calledTool;
            lock (events)
            {
                calledTool = events.Count > 0;
            }

            if (calledTool)
            {
                // Retrying without tools now would let the model answer without the data it asked for.
                return new TurnResult(OllamaFailureAfterToolAnswer, [], Snapshot(events), stopwatch.Elapsed, model, RetriedWithoutTools: false);
            }

            retried = true;
            var withoutTools = options.Clone();
            withoutTools.Tools = null;
            response = await pipeline.GetResponseAsync(messages, withoutTools, turn.Token);
        }

        var toolEvents = Snapshot(events);
        var answer = response.Text.Trim();
        if (toolEvents.Count == 0)
        {
            answer = MetaTextFilter.Clean(answer);
        }

        return new TurnResult(answer, ExtractSources(response), toolEvents, stopwatch.Elapsed, model, retried);
    }

    private static List<ToolTraceEvent> Snapshot(List<ToolTraceEvent> events)
    {
        lock (events)
        {
            return [.. events];
        }
    }

    internal static List<ToolSource> ExtractSources(ChatResponse response) => response.Messages
        .SelectMany(m => m.Contents)
        .OfType<FunctionResultContent>()
        .Select(r => r.Result is JsonElement { ValueKind: JsonValueKind.Object } json
                     && !json.TryGetProperty("error", out _)
                     && json.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String
            ? new ToolSource(source.GetString()!,
                json.TryGetProperty("queried_at", out var at) && at.ValueKind == JsonValueKind.String ? at.GetString() : null)
            : null)
        .OfType<ToolSource>()
        .Distinct()
        .ToList();
}
