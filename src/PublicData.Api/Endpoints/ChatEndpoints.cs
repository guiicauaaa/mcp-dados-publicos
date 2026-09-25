using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading.Channels;
using PublicData.Api.Services;

namespace PublicData.Api.Endpoints;

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat", (ChatRequest request, ChatService chat, ILoggerFactory loggers, CancellationToken cancellationToken) =>
            {
                // The MCP events are published by the tool tracer while the model works, so the turn runs
                // in the background and writes to a channel that the SSE response drains.
                var channel = Channel.CreateUnbounded<SseItem<string>>(new UnboundedChannelOptions { SingleReader = true });
                void Emit(string type, object payload) =>
                    channel.Writer.TryWrite(new SseItem<string>(JsonSerializer.Serialize(payload, payload.GetType(), ChatStreamEvent.Json), type));

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await chat.RunTurnAsync(request, Emit, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // The browser closed the stream.
                    }
                    catch (Exception ex)
                    {
                        loggers.CreateLogger("PublicData.Api.Chat").LogError(ex, "Falha inesperada no turno do chat");
                        Emit(ChatStreamEvent.Error, new MessageEvent("Erro inesperado ao processar a pergunta. Veja o log da API."));
                    }
                    finally
                    {
                        Emit(ChatStreamEvent.Done, new { });
                        channel.Writer.TryComplete();
                    }
                }, CancellationToken.None);

                return TypedResults.ServerSentEvents(channel.Reader.ReadAllAsync(cancellationToken));
            })
            .WithName("Chat")
            .WithTags("Chat")
            .WithSummary("Envia uma pergunta ao modelo local e transmite (SSE) as chamadas MCP e a resposta.")
            .WithDescription("Eventos, em ordem: conversation, tool_call/tool_result (uma dupla por chamada MCP), answer ou error, done.");
    }
}
