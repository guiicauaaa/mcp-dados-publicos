namespace PublicData.Chat.Core.Mcp;

/// <summary>
/// Decides which stderr lines of the MCP server process are worth showing: only the server's own
/// info/warn entries ("Transferegov: consultando…"). The SDK logs a "fail" with a stack trace for every
/// McpException; that stays in the log file.
/// </summary>
public static class ServerLogFilter
{
    public static bool TryFormatForScreen(string line, out string text)
    {
        text = "";
        var levelAt = LevelIndex(line);
        if (levelAt < 0)
        {
            return false;
        }

        var category = line.IndexOf("PublicData.", levelAt, StringComparison.Ordinal);
        if (category < 0)
        {
            return false;
        }

        var endOfCategory = line.IndexOf(']', category);
        if (endOfCategory < 0)
        {
            return false;
        }

        text = line[(endOfCategory + 1)..].Trim();
        return text.Length > 0;
    }

    // Simple console single-line format: "[HH:mm:ss ]info: Category[0] message".
    private static int LevelIndex(string line)
    {
        foreach (var level in (ReadOnlySpan<string>)["info: ", "warn: "])
        {
            var index = line.IndexOf(level, StringComparison.Ordinal);
            if (index >= 0 && index <= 12)
            {
                return index;
            }
        }

        return -1;
    }
}
