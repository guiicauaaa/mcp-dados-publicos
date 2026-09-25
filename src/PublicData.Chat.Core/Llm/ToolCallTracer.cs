using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PublicData.Chat.Core.Llm;

/// <summary>
/// The FunctionInvoker of the FunctionInvokingChatClient: runs the MCP tool, publishes call/result/error events
/// in real time (console, SSE, audit) and hands the model a compact result. A failure never breaks the turn:
/// it becomes {"error": "..."}, which the model explains to the user.
/// </summary>
public static class ToolCallTracer
{
    public static Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> Create(
        Action<ToolTraceEvent> publish,
        TimeSpan timeout,
        TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;

        return async (context, cancellationToken) =>
        {
            var name = context.Function.Name;
            var callId = context.CallContent.CallId;
            var arguments = JsonSerializer.Serialize(context.CallContent.Arguments, ToolResultShaper.CompactJson);
            publish(new ToolTraceEvent(ToolTraceKind.Call, callId, name, arguments, null, null, null, clock.GetUtcNow()));

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            // Over stdio the cancellation does not reach the server (measured with SDK 2.2.0), so bound the wait here.
            timeoutSource.CancelAfter(timeout);
            var stopwatch = Stopwatch.StartNew();

            object? Fail(string message)
            {
                var error = ToolResultShaper.Error(message);
                publish(new ToolTraceEvent(ToolTraceKind.Error, callId, name, arguments, ToolResultShaper.ToJson(error),
                    message, stopwatch.ElapsedMilliseconds, clock.GetUtcNow()));
                return error;
            }

            try
            {
                var raw = await context.Function.InvokeAsync(context.Arguments, timeoutSource.Token);
                var result = ToolResultShaper.Simplify(name, raw);
                var kind = ToolResultShaper.IsError(result) ? ToolTraceKind.Error : ToolTraceKind.Result;
                publish(new ToolTraceEvent(kind, callId, name, arguments, ToolResultShaper.ToJson(result),
                    ToolResultShaper.Preview(result), stopwatch.ElapsedMilliseconds, clock.GetUtcNow()));
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // the user cancelled the turn
            }
            catch (OperationCanceledException)
            {
                return Fail($"A ferramenta {name} não respondeu em {timeout.TotalSeconds:0} s.");
            }
            catch (Exception ex)
            {
                // Server process died, HTTP service down, protocol error.
                return Fail($"Falha ao falar com o servidor MCP: {ex.Message}");
            }
        };
    }
}
