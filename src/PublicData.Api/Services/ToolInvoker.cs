using System.Text.Json;
using Microsoft.Extensions.AI;
using PublicData.Api.Data;
using PublicData.Chat.Core.Llm;

namespace PublicData.Api.Services;

public sealed record ToolInvokeRequest(Dictionary<string, JsonElement>? Arguments);

public sealed record ToolInvokeResponse(string Tool, bool IsError, long ElapsedMs, JsonElement? Result, string Server, string Transport);

/// <summary>
/// Calls an MCP tool directly, without the model: shows that the tool works on its own, and is also audited.
/// It goes through the same tracer as the chat, so the audit row has the same shape.
/// </summary>
public sealed class ToolInvoker(IMcpGateway mcp, AppDbContext db)
{
    public async Task<ToolInvokeResponse?> InvokeAsync(string toolName, ToolInvokeRequest request, CancellationToken cancellationToken)
    {
        var connection = await mcp.GetConnectionAsync(cancellationToken)
            ?? throw new InvalidOperationException(mcp.Status.Message ?? "Servidor MCP indisponível.");
        var tool = connection.Tools.FirstOrDefault(t => t.Name == toolName);
        if (tool is null)
        {
            return null;
        }

        var events = new List<ToolTraceEvent>();
        var invoker = ToolCallTracer.Create(events.Add, TimeSpan.FromSeconds(45));
        var arguments = new AIFunctionArguments((request.Arguments ?? []).ToDictionary(p => p.Key, p => (object?)p.Value));
        var context = new FunctionInvocationContext
        {
            Function = tool,
            Arguments = arguments,
            CallContent = new FunctionCallContent("manual_" + Guid.NewGuid().ToString("N")[..8], toolName, arguments),
        };

        var result = await invoker(context, cancellationToken);
        var server = $"{connection.ServerName} {connection.ServerVersion}";
        db.ToolCalls.AddRange(AuditRecorder.FromEvents(events, ToolCallOrigins.Manual, server, connection.Transport, model: null, conversationId: null));
        await db.SaveChangesAsync(CancellationToken.None);

        var outcome = events.LastOrDefault();
        return new ToolInvokeResponse(toolName, ToolResultShaper.IsError(result), outcome?.ElapsedMs ?? 0,
            ChatService.ParseJson(ToolResultShaper.ToJson(result)), server, connection.Transport);
    }
}
