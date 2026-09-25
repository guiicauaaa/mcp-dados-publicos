using PublicData.Chat.Core.Chat;
using PublicData.Chat.Core.Llm;
using PublicData.Chat.Core.Mcp;
using PublicData.Tests.Support;

namespace PublicData.Tests.Chat;

public sealed class PromptAndModelfileTests
{
    [Fact]
    public void System_prompt_carries_the_Brasilia_date_so_no_tool_is_needed_for_it()
    {
        var prompt = SystemPrompt.Build(BrasiliaClock.Now(FixedTimeProvider.Sept25), "llama3.2-mcp-v1");

        Assert.Contains("Agora são 12:53 de sexta-feira, 25/09/2026 (horário de Brasília).", prompt);
        Assert.Contains("O ano atual é 2026.", prompt);
        Assert.Contains("(llama3.2-mcp-v1)", prompt);
        Assert.EndsWith("\n", prompt);
        Assert.DoesNotContain("Fonte:", prompt);
    }

    [Fact]
    public void Derived_template_replaces_only_the_forced_function_call_paragraph()
    {
        var template = DerivedModel.LoadTemplate();

        Assert.Contains("First decide whether answering the prompt requires data", template);
        Assert.DoesNotContain("Given the following functions", template);
        Assert.DoesNotContain('\r', template);
        Assert.StartsWith("<|start_header_id|>system<|end_header_id|>", template);
        Assert.EndsWith("{{- end }}", template);
    }

    [Fact]
    public void Template_extraction_normalizes_windows_line_endings() =>
        Assert.Equal("a\nb", DerivedModel.ExtractTemplate("FROM x\r\nTEMPLATE \"\"\"a\r\nb\"\"\"\r\n"));

    [Theory]
    [InlineData("12:00:01 info: PublicData.McpServer.Tools.AmendmentTools[0] Transferegov: 8 planos de ação em 433 ms", "Transferegov: 8 planos de ação em 433 ms")]
    [InlineData("warn: PublicData.McpServer.Tools.AmendmentTools[0] Transferegov falhou em 20000 ms", "Transferegov falhou em 20000 ms")]
    public void Server_log_filter_shows_the_server_own_lines(string line, string expected)
    {
        Assert.True(ServerLogFilter.TryFormatForScreen(line, out var text));
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("12:00:01 fail: ModelContextProtocol.Server.McpServer[0] Tool call failed")]
    [InlineData("12:00:01 info: Microsoft.Hosting.Lifetime[0] Application started.")]
    [InlineData("   at System.Threading.Tasks.Task.Wait()")]
    public void Server_log_filter_hides_sdk_noise(string line) => Assert.False(ServerLogFilter.TryFormatForScreen(line, out _));

    [Fact]
    public void Sdk_failure_line_is_parsed_without_the_stack_trace()
    {
        var entry = ServerLogFilter.Parse("14:00:14 fail: ModelContextProtocol.Server.McpServer[1433779783] \"get_city_amendments\" threw an unhandled exception. ModelContextProtocol.McpException: UF 'XX' inválida.    at PublicData.McpServer.Tools.AmendmentTools.GetCityAmendments()");

        Assert.Equal("14:00:14", entry.Time);
        Assert.Equal("fail", entry.Level);
        Assert.Equal("ModelContextProtocol.Server.McpServer", entry.Category);
        Assert.EndsWith("UF 'XX' inválida.", entry.Message);
        Assert.False(entry.IsServerOwn);
    }
}
