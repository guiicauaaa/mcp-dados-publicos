using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace PublicData.McpServer.Tools;

public static class ToolJson
{
    /// <summary>
    /// The default encoder escapes every non-ASCII char, so the model would read "Educação".
    /// Relaxed escaping keeps "Educação" readable in the text block the model receives.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
