using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PublicData.Api.Data;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Chat;
using PublicData.Chat.Core.Llm;

namespace PublicData.Api.Services;

public sealed record ChatRequest(Guid? ConversationId, string Message);

/// <summary>One chat turn over HTTP: history from the database, real-time MCP events, answer and audit trail.</summary>
public sealed class ChatService(
    AppDbContext db,
    IModelGateway model,
    IMcpGateway mcp,
    ChatSettings settings,
    TimeProvider clock,
    ILogger<ChatService> logger)
{
    public const int MaxMessageLength = 2000;
    private const int MaxTitleLength = 80;

    /// <param name="emit">Synchronous: called from the tool tracer while the model works.</param>
    public async Task RunTurnAsync(ChatRequest request, Action<string, object> emit, CancellationToken cancellationToken)
    {
        var question = request.Message?.Trim() ?? "";
        if (question.Length == 0 || question.Length > MaxMessageLength)
        {
            emit(ChatStreamEvent.Error, new MessageEvent($"A pergunta precisa ter entre 1 e {MaxMessageLength} caracteres."));
            return;
        }

        if (!await model.EnsureReadyAsync(cancellationToken) || model.Client is not { } client || model.Model is not { } modelName)
        {
            emit(ChatStreamEvent.Error, new MessageEvent(model.Status.Message ?? "O modelo local ainda não está pronto."));
            return;
        }

        var connection = await mcp.GetConnectionAsync(cancellationToken);
        IReadOnlyList<AITool> tools = connection is null
            ? []
            : [.. connection.Tools.Where(t => settings.EffectiveExposedTools.Contains(t.Name))];
        if (connection is null)
        {
            emit(ChatStreamEvent.Warning, new MessageEvent(
                $"Servidor MCP indisponível ({mcp.Status.Message}). Respondendo sem consultar dados externos."));
        }

        var conversation = await LoadOrCreateConversationAsync(request.ConversationId, question, cancellationToken);
        var history = await LoadHistoryAsync(conversation.Id, cancellationToken);

        var now = clock.GetUtcNow();
        db.Messages.Add(new ConversationMessage { ConversationId = conversation.Id, Role = MessageRoles.User, Content = question, CreatedAt = now });
        conversation.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        emit(ChatStreamEvent.Conversation, new ConversationEvent(conversation.Id, conversation.Title));

        // Kept here too, so the MCP calls that already happened are audited even if the turn never ends.
        var traced = new List<ToolTraceEvent>();
        void OnToolEvent(ToolTraceEvent e)
        {
            lock (traced)
            {
                traced.Add(e);
            }

            if (e.Kind == ToolTraceKind.Call)
            {
                emit(ChatStreamEvent.ToolCall, new ToolCallEvent(e.CallId, e.ToolName, ParseJson(e.ArgumentsJson) ?? default, e.Timestamp));
            }
            else
            {
                emit(ChatStreamEvent.ToolResult, new ToolResultEvent(e.CallId, e.ToolName, e.Kind == ToolTraceKind.Error,
                    e.ElapsedMs ?? 0, e.ResultPreview, ParseJson(e.ResultJson)));
            }
        }

        var server = connection is null ? "-" : $"{connection.ServerName} {connection.ServerVersion}";
        var transport = connection?.Transport ?? "-";
        var stopwatch = Stopwatch.StartNew();

        TurnResult result;
        try
        {
            result = await new ChatEngine(client, settings, clock).AskAsync(history, question, tools, modelName, OnToolEvent, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Falha ao falar com o Ollama: {Message}", ex.Message);
            model.ReportFailure(ex.Message);
            var message = $"Perdi a conexão com o Ollama em {settings.OllamaBaseUrl}. Verifique se ele está aberto e tente de novo.";
            await SaveInterruptedTurnAsync(conversation, message, Snapshot(traced), modelName, server, transport, stopwatch.Elapsed);
            emit(ChatStreamEvent.Error, new MessageEvent(message));
            return;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var message = $"A resposta passou de {settings.TurnTimeout.TotalMinutes:0} minutos. Tente de novo (o modelo pode estar carregando).";
            await SaveInterruptedTurnAsync(conversation, message, Snapshot(traced), modelName, server, transport, stopwatch.Elapsed);
            emit(ChatStreamEvent.Error, new MessageEvent(message));
            return;
        }
        catch (OperationCanceledException)
        {
            // The user pressed "Parar" or closed the page: what already happened (MCP calls included) stays recorded.
            await SaveInterruptedTurnAsync(conversation, CancelledAnswer, Snapshot(traced), modelName, server, transport, stopwatch.Elapsed);
            throw;
        }

        var answer = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRoles.Assistant,
            Content = result.Answer,
            Model = result.Model,
            ElapsedMs = (int)result.Elapsed.TotalMilliseconds,
            SourceLine = result.SourceLine,
            CreatedAt = clock.GetUtcNow(),
        };
        answer.ToolCalls.AddRange(AuditRecorder.FromEvents(result.ToolEvents, ToolCallOrigins.Chat, server, transport, result.Model, conversation.Id));
        db.Messages.Add(answer);
        conversation.UpdatedAt = answer.CreatedAt;

        // The audit trail is kept even if the browser closed the stream meanwhile.
        await db.SaveChangesAsync(CancellationToken.None);

        emit(ChatStreamEvent.Answer, new AnswerEvent(answer.Id, result.Answer, result.Model, answer.ElapsedMs ?? 0, result.SourceLine, result.UsedTools));
    }

    public const string CancelledAnswer = "(resposta cancelada antes de terminar)";

    /// <summary>
    /// A turn without an answer still records an assistant message (so the history keeps alternating) and the
    /// audit rows of the MCP calls that already reached the server.
    /// </summary>
    private async Task SaveInterruptedTurnAsync(Conversation conversation, string note, IReadOnlyList<ToolTraceEvent> events,
        string modelName, string server, string transport, TimeSpan elapsed)
    {
        var message = new ConversationMessage
        {
            ConversationId = conversation.Id,
            Role = MessageRoles.Assistant,
            Content = note,
            Model = modelName,
            ElapsedMs = (int)elapsed.TotalMilliseconds,
            CreatedAt = clock.GetUtcNow(),
        };
        message.ToolCalls.AddRange(AuditRecorder.FromEvents(events, ToolCallOrigins.Chat, server, transport, modelName, conversation.Id));
        db.Messages.Add(message);
        conversation.UpdatedAt = message.CreatedAt;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Cuts on a character boundary: a lone surrogate (half an emoji) makes PostgreSQL reject the text.</summary>
    internal static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength - 1;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }

    private static List<ToolTraceEvent> Snapshot(List<ToolTraceEvent> events)
    {
        lock (events)
        {
            return [.. events];
        }
    }

    private async Task<Conversation> LoadOrCreateConversationAsync(Guid? id, string question, CancellationToken cancellationToken)
    {
        if (id is { } existingId && await db.Conversations.FindAsync([existingId], cancellationToken) is { } existing)
        {
            return existing;
        }

        var now = clock.GetUtcNow();
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            Title = Truncate(question, MaxTitleLength),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Conversations.Add(conversation);
        return conversation;
    }

    /// <summary>Text only: the engine never sees old tool calls or results (measured to confuse a 3B model).</summary>
    private async Task<List<ChatMessage>> LoadHistoryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var recent = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(settings.HistoryMessages)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);

        recent.Reverse();
        return [.. recent.Select(m => new ChatMessage(m.Role == MessageRoles.User ? ChatRole.User : ChatRole.Assistant, m.Content))];
    }

    internal static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(json);
        }
    }
}
