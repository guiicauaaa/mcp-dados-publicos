using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace PublicData.Chat.Core.Llm;

/// <summary>
/// Turns what the MCP client returns into what a 3B model reads best. Without it, OllamaSharp would send the
/// TextContent object itself: JSON escaped inside JSON, plus Annotations and AdditionalProperties noise.
/// </summary>
public static partial class ToolResultShaper
{
    public static readonly JsonSerializerOptions CompactJson = new(AIJsonUtilities.DefaultOptions)
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static object? Simplify(string toolName, object? result)
    {
        switch (result)
        {
            case TextContent text:
                return TryParseJson(text.Text) ?? (object)text.Text;

            case JsonElement { ValueKind: JsonValueKind.Object } element
                when element.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array:
                // Raw CallToolResult: the SDK does not unwrap it when isError=true or structuredContent exists.
                var joined = string.Join("\n", blocks.EnumerateArray()
                    .Where(b => b.TryGetProperty("text", out _))
                    .Select(b => b.GetProperty("text").GetString()));
                var isError = element.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
                return isError ? Error(ErrorMessage(toolName, joined)) : TryParseJson(joined) ?? (object)joined;

            default:
                return result;
        }
    }

    public static JsonElement Error(string message) => JsonSerializer.SerializeToElement(new { error = message }, CompactJson);

    public static bool IsError(object? shaped) =>
        shaped is JsonElement { ValueKind: JsonValueKind.Object } element && element.TryGetProperty("error", out _);

    public static string ToJson(object? value) => value switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(s, CompactJson),
        JsonElement element => JsonSerializer.Serialize(element, CompactJson),
        _ => JsonSerializer.Serialize(value, CompactJson),
    };

    public static string Preview(object? result, int max = 220)
    {
        var text = result is string s ? s : ToJson(result);
        return text.Length <= max ? text : text[..max] + "...";
    }

    /// <summary>
    /// McpException messages arrive as "An error occurred invoking 'tool': message". Anything else, like a
    /// binding failure for year="este ano", arrives as the bare English sentence: replace it with pt-BR guidance.
    /// </summary>
    internal static string ErrorMessage(string toolName, string sdkText)
    {
        var withoutPrefix = SdkErrorPrefix().Replace(sdkText, "").Trim();
        return SdkGenericError().IsMatch(withoutPrefix) || withoutPrefix.Length == 0
            ? $"Argumentos inválidos para a ferramenta {toolName}. Confira os valores (ano com 4 dígitos, UF com 2 letras) e tente de novo."
            : withoutPrefix;
    }

    private static JsonElement? TryParseJson(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^An error occurred invoking '[^']+':\s*")]
    private static partial Regex SdkErrorPrefix();

    [GeneratedRegex(@"^An error occurred invoking '[^']+'\.?$")]
    private static partial Regex SdkGenericError();
}
