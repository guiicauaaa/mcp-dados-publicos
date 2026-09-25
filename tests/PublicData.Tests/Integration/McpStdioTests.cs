using System.Text.Json;
using ModelContextProtocol.Protocol;
using PublicData.Tests.Support;

namespace PublicData.Tests.Integration;

/// <summary>Real server process over stdio, through the official SDK client. No case reaches the network.</summary>
public sealed class McpStdioTests(McpServerFixture server) : IClassFixture<McpServerFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private async Task<(bool IsError, string Text)> Call(Dictionary<string, object?> arguments, string tool = "get_city_amendments")
    {
        var result = await server.Client.CallToolAsync(tool, arguments, cancellationToken: Ct);
        return (result.IsError == true, string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text)));
    }

    [Fact]
    public void Handshake_uses_the_current_protocol_and_server_identity()
    {
        Assert.Equal("public-data-mcp", server.Client.ServerInfo.Name);
        Assert.Equal("2026-07-28", server.Client.NegotiatedProtocolVersion);
    }

    [Fact]
    public void Tools_list_describes_both_tools()
    {
        var amendments = Assert.Single(server.Tools, t => t.Name == "get_city_amendments");
        Assert.Contains(server.Tools, t => t.Name == "get_current_datetime");

        var schema = amendments.JsonSchema;
        Assert.Equal(["city"], schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.True(schema.GetProperty("properties").TryGetProperty("year", out var year));
        Assert.Equal("integer", year.GetProperty("type").GetString());
        Assert.True(amendments.ProtocolTool.Annotations?.ReadOnlyHint);
    }

    [Fact]
    public async Task Invalid_state_comes_back_as_tool_error_with_accents_intact()
    {
        var (isError, text) = await Call(new() { ["city"] = "Campinas", ["state"] = "XX", ["year"] = 2026 });

        Assert.True(isError);
        Assert.Contains("UF 'XX' inválida", text);
    }

    [Fact]
    public async Task Ambiguous_city_lists_the_options()
    {
        var (isError, text) = await Call(new() { ["city"] = "Santa Rita", ["year"] = 2026 });

        Assert.True(isError);
        Assert.Contains("Santa Rita - MA", text);
        Assert.Contains("Santa Rita - PB", text);
    }

    [Fact]
    public async Task Year_sent_as_string_is_accepted_by_the_binder()
    {
        // llama3.2 sends "2026" as a string in about half of the calls. The call reaching the UF check
        // (and not a binding error) proves the string was accepted as an integer.
        var (isError, text) = await Call(new() { ["city"] = "Campinas", ["state"] = "XX", ["year"] = "2026" });

        Assert.True(isError);
        Assert.Contains("UF 'XX' inválida", text);
    }

    [Fact]
    public async Task Server_logs_reach_stderr_as_utf8_single_lines()
    {
        await Call(new() { ["city"] = "Campinas", ["state"] = "XX", ["year"] = 2026 });

        // Without Console.OutputEncoding = UTF-8 in the server, Windows would send "inv�lida".
        for (var i = 0; i < 50 && !server.StderrLines.Any(l => l.Contains("inválida", StringComparison.Ordinal)); i++)
        {
            await Task.Delay(100, Ct);
        }

        // Other tests of this class share the process and log the same refusal, so any matching line will do.
        Assert.Contains(server.StderrLines, l =>
            l.Contains("info: PublicData.McpServer.Tools.AmendmentTools", StringComparison.Ordinal)
            && l.Contains("get_city_amendments recusado: UF 'XX' inválida", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Datetime_tool_answers_without_network()
    {
        var (isError, text) = await Call([], "get_current_datetime");

        Assert.False(isError);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("America/Sao_Paulo (horário de Brasília)", json.RootElement.GetProperty("timezone").GetString());
    }
}
