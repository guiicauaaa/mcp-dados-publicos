namespace PublicData.Chat.Core.Llm;

/// <summary>
/// llama3.2 with the same weights and a fixed tool template (ollama/Modelfile, embedded). The official template
/// injects "respond with a JSON for a function call" whenever tools exist, so the model called a tool even for
/// "oi". The suffix changes whenever the template changes, because an existing model is never recreated.
/// </summary>
public static class DerivedModel
{
    public const string Name = "llama3.2-mcp-v1";
    public const string BaseModel = "llama3.2";

    private const string TemplateStart = "TEMPLATE \"\"\"";
    private const string TemplateEnd = "\"\"\"";

    /// <summary>The TEMPLATE block of the embedded Modelfile, with LF line endings (Git on Windows may add CR).</summary>
    public static string LoadTemplate()
    {
        using var stream = typeof(DerivedModel).Assembly.GetManifestResourceStream("Modelfile")
            ?? throw new InvalidOperationException("Recurso Modelfile não encontrado no assembly.");
        using var reader = new StreamReader(stream);
        return ExtractTemplate(reader.ReadToEnd());
    }

    public static string ExtractTemplate(string modelfile)
    {
        var text = modelfile.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf(TemplateStart, StringComparison.Ordinal);
        var end = text.LastIndexOf(TemplateEnd, StringComparison.Ordinal);
        if (start < 0 || end <= start + TemplateStart.Length)
        {
            throw new InvalidOperationException("Modelfile sem bloco TEMPLATE \"\"\"...\"\"\".");
        }

        start += TemplateStart.Length;
        return text[start..end];
    }
}
