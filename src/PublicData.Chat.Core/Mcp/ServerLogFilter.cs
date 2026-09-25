using System.Text.RegularExpressions;

namespace PublicData.Chat.Core.Mcp;

/// <summary>A stderr line of the MCP server process, split into its parts.</summary>
/// <param name="IsServerOwn">True for the server's own entries (category PublicData.*), false for SDK and hosting noise.</param>
public sealed record ServerLogEntry(string? Time, string? Level, string? Category, string Message, bool IsServerOwn);

/// <summary>
/// Reads the server's single-line console format ("HH:mm:ss info: Category[0] message"). The SDK logs a "fail"
/// with a stack trace for every McpException (an expected validation refusal); the stack trace is cut and the
/// line is marked as not the server's own, so screens can hide it.
/// </summary>
public static partial class ServerLogFilter
{
    public static ServerLogEntry Parse(string line)
    {
        var match = LogLine().Match(line);
        if (!match.Success)
        {
            return new ServerLogEntry(null, null, null, line.Trim(), false);
        }

        var message = match.Groups["message"].Value;
        var stack = message.IndexOf("    at ", StringComparison.Ordinal);
        if (stack > 0)
        {
            message = message[..stack];
        }

        var category = match.Groups["category"].Value;
        return new ServerLogEntry(
            match.Groups["time"].Success ? match.Groups["time"].Value : null,
            match.Groups["level"].Value,
            category,
            message.Trim(),
            category.StartsWith("PublicData.", StringComparison.Ordinal));
    }

    /// <summary>Only the server's own info/warn entries ("Transferegov: consultando…") go to the console screen.</summary>
    public static bool TryFormatForScreen(string line, out string text)
    {
        var entry = Parse(line);
        var visible = entry.IsServerOwn && entry.Level is "info" or "warn" && entry.Message.Length > 0;
        text = visible ? entry.Message : "";
        return visible;
    }

    [GeneratedRegex(@"^(?:(?<time>\d{2}:\d{2}:\d{2})\s+)?(?<level>trce|dbug|info|warn|fail|crit): (?<category>[^\[\s]+)\[\d+\]\s*(?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex LogLine();
}
