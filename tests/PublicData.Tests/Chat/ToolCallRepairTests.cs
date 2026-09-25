using System.Text.Json;
using Microsoft.Extensions.AI;
using PublicData.Chat.Core.Llm;

namespace PublicData.Tests.Chat;

public sealed class ToolCallRepairTests
{
    private static readonly AIFunction Amendments = AIFunctionFactory.Create(
        (string city, string state = "", int year = 0) => "ok", "get_city_amendments");

    private static readonly AIFunction WithParametersArgument = AIFunctionFactory.Create(
        (string parameters) => "ok", "raw_tool");

    [Fact]
    public void Tool_call_written_as_fenced_json_text_is_recovered()
    {
        const string text = "```json\n{\"name\": \"get_city_amendments\", \"parameters\": {\"city\": \"Campinas\", \"year\": 2026}}\n```";

        Assert.True(ToolCallRepairChatClient.TryParseTextCall(text, [Amendments], out var call));
        Assert.Equal("get_city_amendments", call.Name);
        Assert.Equal("Campinas", ((JsonElement)call.Arguments!["city"]!).GetString());
    }

    [Theory]
    [InlineData("{\"name\": \"delete_everything\", \"parameters\": {}}")]
    [InlineData("Campinas recebeu R$ 7,91 milhões.")]
    [InlineData("")]
    public void Unknown_tools_and_plain_text_are_left_alone(string text) =>
        Assert.False(ToolCallRepairChatClient.TryParseTextCall(text, [Amendments], out _));

    [Fact]
    public void Wrapped_arguments_are_unwrapped()
    {
        // Measured with llama3.2: {"type":"function","function":"get_city_amendments","parameters":{...}} as the arguments.
        var wrapped = JsonDocument.Parse("""{"type":"function","function":"get_city_amendments","parameters":{"city":"Campinas","state":"SP"}}""").RootElement;
        var arguments = wrapped.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        var call = new FunctionCallContent("c1", "get_city_amendments", arguments);

        Assert.True(ToolCallRepairChatClient.TryUnwrapArguments(call, [Amendments], out var fixedCall));
        Assert.Equal("c1", fixedCall.CallId);
        Assert.Equal(["city", "state"], fixedCall.Arguments!.Keys.Order().ToArray());
    }

    [Fact]
    public void Tool_that_really_has_a_parameters_argument_is_not_unwrapped()
    {
        var call = new FunctionCallContent("c1", "raw_tool", new Dictionary<string, object?>
        {
            ["name"] = "x",
            ["parameters"] = JsonDocument.Parse("{\"a\":1}").RootElement,
        });

        Assert.False(ToolCallRepairChatClient.TryUnwrapArguments(call, [WithParametersArgument], out _));
    }

    [Fact]
    public void Normal_arguments_are_not_touched()
    {
        var call = new FunctionCallContent("c1", "get_city_amendments", new Dictionary<string, object?> { ["city"] = "Recife" });

        Assert.False(ToolCallRepairChatClient.TryUnwrapArguments(call, [Amendments], out _));
    }
}
