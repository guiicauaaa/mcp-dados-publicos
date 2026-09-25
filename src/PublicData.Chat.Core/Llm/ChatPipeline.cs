using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;

namespace PublicData.Chat.Core.Llm;

public static class ChatPipeline
{
    /// <summary>FunctionInvoking (with the tracer) wraps ToolCallRepair, which wraps the Ollama client.</summary>
    public static IChatClient Build(IChatClient inner, Action<ToolTraceEvent> publish, TimeSpan toolTimeout, TimeProvider? clock = null) =>
        new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: invoking =>
            {
                // On the last iteration the tools are removed, which forces a text answer: no endless loops.
                invoking.MaximumIterationsPerRequest = 3;
                invoking.MaximumConsecutiveErrorsPerRequest = 2;
                invoking.IncludeDetailedErrors = true;
                invoking.FunctionInvoker = ToolCallTracer.Create(publish, toolTimeout, clock);
            })
            .Use(client => new ToolCallRepairChatClient(client))
            .Build();

    public static ChatOptions CreateOptions(IEnumerable<AITool> tools, ChatSettings settings, string model)
    {
        var options = new ChatOptions
        {
            ModelId = model,
            Tools = [.. tools],
            Temperature = settings.Temperature,
            Seed = settings.Seed,
            MaxOutputTokens = settings.MaxOutputTokens,
        }.AddOllamaOption(OllamaOption.NumCtx, settings.NumCtx);

        // Must be a string: the OllamaSharp mapper casts keep_alive to string (an int or TimeSpan breaks the cast).
        options.AdditionalProperties!["keep_alive"] = settings.KeepAlive;
        return options;
    }
}
