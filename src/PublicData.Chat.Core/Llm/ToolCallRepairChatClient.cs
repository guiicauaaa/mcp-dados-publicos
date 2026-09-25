using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PublicData.Chat.Core.Llm;

/// <summary>
/// Safety net for two llama3.2 (3B) defects, placed between the FunctionInvokingChatClient and Ollama:
/// <list type="number">
///   <item>the tool call written as text (```json {"name": …, "parameters": …}```) instead of a tool_call
///   (ollama/ollama#13519): converted into a FunctionCallContent, only for known tools;</item>
///   <item>a native tool_call whose arguments wrap the real ones, like
///   {"type":"function","function":"get_city_amendments","parameters":{"city":"Campinas"}}: unwrapped.</item>
/// </list>
/// Non-streaming only: the chat uses GetResponseAsync, which is what makes this repair possible.
/// </summary>
public sealed class ToolCallRepairChatClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    private static readonly string[] WrapperKeys = ["type", "function", "name"];

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        var tools = options?.Tools?.OfType<AIFunctionDeclaration>().ToList() ?? [];
        if (tools.Count == 0)
        {
            return response;
        }

        var hasNativeCall = false;
        foreach (var message in response.Messages)
        {
            for (var i = 0; i < message.Contents.Count; i++)
            {
                if (message.Contents[i] is FunctionCallContent call)
                {
                    hasNativeCall = true;
                    if (TryUnwrapArguments(call, tools, out var fixedCall))
                    {
                        message.Contents[i] = fixedCall;
                    }
                }
            }
        }

        if (!hasNativeCall && TryParseTextCall(response.Text, tools, out var recovered))
        {
            response.Messages = [new ChatMessage(ChatRole.Assistant, [recovered])];
            response.FinishReason = ChatFinishReason.ToolCalls;
        }

        return response;
    }

    internal static bool TryUnwrapArguments(FunctionCallContent call, IReadOnlyList<AIFunctionDeclaration> tools, out FunctionCallContent fixedCall)
    {
        fixedCall = call;
        if (call.Arguments is not { } arguments
            || !arguments.TryGetValue("parameters", out var inner)
            || !arguments.Keys.Any(k => WrapperKeys.Contains(k, StringComparer.Ordinal)))
        {
            return false;
        }

        // Never unwrap a tool that really has a "parameters" argument.
        var tool = tools.FirstOrDefault(t => t.Name == call.Name);
        if (tool is null || DeclaresProperty(tool, "parameters"))
        {
            return false;
        }

        var unwrapped = ToDictionary(inner);
        if (unwrapped is null)
        {
            return false;
        }

        fixedCall = new FunctionCallContent(call.CallId, call.Name, unwrapped);
        return true;
    }

    internal static bool TryParseTextCall(string? text, IReadOnlyList<AIFunctionDeclaration> tools, out FunctionCallContent call)
    {
        call = null!;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var json = text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine < 0 || lastFence <= firstNewLine)
            {
                return false;
            }

            json = json[(firstNewLine + 1)..lastFence].Trim();
        }

        if (!json.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()
                : root.TryGetProperty("function", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString()
                : null;
            if (name is null || !tools.Any(t => t.Name == name))
            {
                return false; // unknown names are ignored: the text is shown as the answer
            }

            var arguments = new Dictionary<string, object?>();
            if ((root.TryGetProperty("parameters", out var p) || root.TryGetProperty("arguments", out p)) && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in p.EnumerateObject())
                {
                    arguments[property.Name] = property.Value.Clone();
                }
            }

            call = new FunctionCallContent("txt_" + Guid.NewGuid().ToString("N")[..8], name, arguments);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool DeclaresProperty(AIFunctionDeclaration tool, string property) =>
        tool.JsonSchema.ValueKind == JsonValueKind.Object
        && tool.JsonSchema.TryGetProperty("properties", out var properties)
        && properties.ValueKind == JsonValueKind.Object
        && properties.TryGetProperty(property, out _);

    private static Dictionary<string, object?>? ToDictionary(object? value)
    {
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.Object } element:
                return element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            case IDictionary<string, object?> dictionary:
                return new Dictionary<string, object?>(dictionary);
            case string s when s.TrimStart().StartsWith('{'):
                try
                {
                    using var document = JsonDocument.Parse(s);
                    return ToDictionary(document.RootElement.Clone());
                }
                catch (JsonException)
                {
                    return null;
                }

            default:
                return null;
        }
    }
}
