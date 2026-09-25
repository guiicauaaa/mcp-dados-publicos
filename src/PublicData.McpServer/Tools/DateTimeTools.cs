using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using PublicData.McpServer.Time;

namespace PublicData.McpServer.Tools;

/// <summary>
/// The server date/time tool, the same idea as <c>get_time</c> in the reference repository, now with the
/// Brasília time zone. The chat does not offer it to llama3.2 by default (measured: it made the 3B model call
/// tools on greetings); the current date goes in the system prompt instead. Other MCP clients can use it.
/// </summary>
[McpServerToolType]
public sealed class DateTimeTools(TimeProvider clock)
{
    public const string ToolName = "get_current_datetime";
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    [McpServerTool(Name = ToolName, Title = "Data e hora atuais (Brasília)",
        ReadOnly = true, Idempotent = false, OpenWorld = false, UseStructuredContent = false)]
    [Description("Returns the current date and time in Brasília (America/Sao_Paulo). Use ONLY when the user asks what day, date or time it is now.")]
    public CurrentDateTime GetCurrentDateTime()
    {
        var now = BrasiliaTime.Now(clock);
        return new CurrentDateTime(
            now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            now.ToString("dd/MM/yyyy", PtBr),
            now.ToString("HH:mm", PtBr),
            now.ToString("dddd", PtBr),
            "America/Sao_Paulo (horário de Brasília)",
            "Relógio do servidor MCP");
    }
}

public sealed record CurrentDateTime(
    [property: JsonPropertyName("now")] string Now,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("time")] string Time,
    [property: JsonPropertyName("weekday")] string Weekday,
    [property: JsonPropertyName("timezone")] string TimeZone,
    [property: JsonPropertyName("source")] string Source);
