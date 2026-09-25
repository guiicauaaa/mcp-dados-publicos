using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using PublicData.Chat.Core;
using PublicData.Chat.Core.Chat;
using PublicData.Chat.Core.Llm;
using PublicData.Tests.Support;

namespace PublicData.Tests.Chat;

/// <summary>
/// The production pipeline (FunctionInvoking + tracer + repair) with a scripted model in place of Ollama and,
/// where it matters, the REAL MCP server over stdio.
/// </summary>
public sealed class ChatEngineTests(McpServerFixture server) : IClassFixture<McpServerFixture>
{
    private static readonly ChatSettings Settings = new() { ToolTimeout = TimeSpan.FromSeconds(30) };
    private const string Model = "llama3.2-mcp-v1";

    private static ChatEngine Engine(ScriptedChatClient model) => new(model, Settings, FixedTimeProvider.Sept25);

    private IReadOnlyList<AITool> McpTools => [.. server.Tools.Where(t => t.Name == "get_city_amendments")];

    [Fact]
    public async Task Tool_error_from_the_real_mcp_server_reaches_the_model_as_a_readable_error()
    {
        var model = new ScriptedChatClient(
            (_, _) => ScriptedChatClient.Call("get_city_amendments", new() { ["city"] = "Campinas", ["state"] = "XX" }),
            (_, _) => ScriptedChatClient.Text("Essa UF não existe. Qual é a UF do município?"));
        var events = new List<ToolTraceEvent>();

        var result = await Engine(model).AskAsync([], "Quanto Campinas recebeu?", McpTools, Model, events.Add, TestContext.Current.CancellationToken);

        Assert.Equal("Essa UF não existe. Qual é a UF do município?", result.Answer);
        Assert.Equal([ToolTraceKind.Call, ToolTraceKind.Error], events.Select(e => e.Kind));
        var toolMessage = model.Requests[1].Messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single();
        var error = Assert.IsType<JsonElement>(toolMessage.Result).GetProperty("error").GetString();
        Assert.StartsWith("UF 'XX' inválida", error);
        Assert.Null(result.SourceLine);
    }

    [Fact]
    public async Task Only_the_text_of_recent_turns_is_sent_as_history()
    {
        var model = new ScriptedChatClient((_, _) => ScriptedChatClient.Text("ok"));
        List<ChatMessage> history = [];
        for (var i = 1; i <= 5; i++)
        {
            history.Add(new ChatMessage(ChatRole.User, $"pergunta {i}"));
            history.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"c{i}", "get_city_amendments")]));
            history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent($"c{i}", "{}")]));
            history.Add(new ChatMessage(ChatRole.Assistant, $"resposta {i}"));
        }

        await Engine(model).AskAsync(history, "E em 2025?", McpTools, Model, cancellationToken: TestContext.Current.CancellationToken);

        var sent = model.Requests[0].Messages;
        Assert.Equal(Settings.HistoryMessages + 1, sent.Count);
        Assert.All(sent, m => Assert.True(m.Role == ChatRole.User || m.Role == ChatRole.Assistant));
        Assert.All(sent, m => Assert.All(m.Contents, c => Assert.IsType<TextContent>(c)));
        Assert.Equal("E em 2025?", sent[^1].Text);
        Assert.Equal("pergunta 3", sent[0].Text);
    }

    [Fact]
    public async Task System_prompt_is_rebuilt_with_the_current_date_and_model()
    {
        var model = new ScriptedChatClient((_, _) => ScriptedChatClient.Text("ok"));

        await Engine(model).AskAsync([], "oi", McpTools, Model, cancellationToken: TestContext.Current.CancellationToken);

        var options = model.Requests[0].Options!;
        Assert.Contains("sexta-feira, 25/09/2026", options.Instructions);
        Assert.Equal(Model, options.ModelId);
        Assert.Equal("30m", options.AdditionalProperties!["keep_alive"]);
    }

    [Fact]
    public async Task Source_line_comes_from_the_tool_result_not_from_the_model()
    {
        var tool = new LocalTool("fake_source", _ => new TextContent(
            """{"total":"R$ 7,91 milhões","source":"Transferegov - Transferências Especiais (emendas Pix), governo federal","queried_at":"25/09/2026 12:36"}"""));
        var model = new ScriptedChatClient(
            (_, _) => ScriptedChatClient.Call("fake_source", []),
            (_, _) => ScriptedChatClient.Text("Campinas recebeu R$ 7,91 milhões. Fonte: inventada pelo modelo"));

        var result = await Engine(model).AskAsync([], "Quanto?", [tool], Model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Fonte: Transferegov - Transferências Especiais (emendas Pix), governo federal (consultado via MCP em 25/09/2026 12:36)",
            result.SourceLine);
        Assert.True(result.UsedTools);
    }

    [Fact]
    public async Task Ollama_500_without_any_tool_call_is_retried_without_tools()
    {
        var model = new ScriptedChatClient(
            (_, _) => throw new HttpRequestException("token repeat limit reached", null, HttpStatusCode.InternalServerError),
            (_, _) => ScriptedChatClient.Text("Até mais!"));

        var result = await Engine(model).AskAsync([], "tchau", McpTools, Model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.RetriedWithoutTools);
        Assert.Equal("Até mais!", result.Answer);
        Assert.True(model.Requests[1].Options!.Tools is null or { Count: 0 });
    }

    [Fact]
    public async Task Ollama_500_after_a_tool_call_is_not_retried_so_numbers_are_never_made_up()
    {
        var tool = new LocalTool("fake_source", _ => new TextContent("{\"total\":\"R$ 1,00\"}"));
        var model = new ScriptedChatClient(
            (_, _) => ScriptedChatClient.Call("fake_source", []),
            (_, _) => throw new HttpRequestException("token repeat limit reached", null, HttpStatusCode.InternalServerError));

        var result = await Engine(model).AskAsync([], "Quanto?", [tool], Model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ChatEngine.OllamaFailureAfterToolAnswer, result.Answer);
        Assert.False(result.RetriedWithoutTools);
        Assert.Equal(2, model.Requests.Count);
    }

    [Fact]
    public async Task A_model_that_keeps_calling_tools_is_stopped_and_the_last_request_has_no_tools()
    {
        var tool = new LocalTool("fake_source", _ => new TextContent("{}"));
        var model = new ScriptedChatClient((_, _) => ScriptedChatClient.Call("fake_source", [], Guid.NewGuid().ToString("N")));

        await Engine(model).AskAsync([], "loop", [tool], Model, cancellationToken: TestContext.Current.CancellationToken);

        Assert.InRange(model.Requests.Count, 2, 4);
        Assert.True(model.Requests[^1].Options!.Tools is null or { Count: 0 });
    }

    [Fact]
    public async Task Tool_call_written_as_text_is_executed_against_the_real_server()
    {
        var model = new ScriptedChatClient(
            (_, _) => ScriptedChatClient.Text("```json\n{\"name\": \"get_city_amendments\", \"parameters\": {\"city\": \"Santa Rita\"}}\n```"),
            (_, _) => ScriptedChatClient.Text("Há duas Santa Rita. Qual UF?"));
        var events = new List<ToolTraceEvent>();

        var result = await Engine(model).AskAsync([], "Quanto Santa Rita recebeu?", McpTools, Model, events.Add, TestContext.Current.CancellationToken);

        Assert.Equal("Há duas Santa Rita. Qual UF?", result.Answer);
        var error = Assert.Single(events, e => e.Kind == ToolTraceKind.Error);
        Assert.Contains("Santa Rita - PB", error.ResultPreview);
    }
}
