using System.Text.Json;
using Microsoft.Extensions.AI;
using PublicData.Chat.Core.Llm;

namespace PublicData.Tests.Chat;

public sealed class ToolResultShaperTests
{
    [Fact]
    public void Text_content_with_json_becomes_a_json_element()
    {
        var shaped = ToolResultShaper.Simplify("t", new TextContent("{\"total\":\"R$ 7,91 milhões\"}"));

        var json = Assert.IsType<JsonElement>(shaped);
        Assert.Equal("R$ 7,91 milhões", json.GetProperty("total").GetString());
    }

    [Fact]
    public void Raw_error_result_becomes_error_object_without_the_sdk_prefix()
    {
        var raw = JsonDocument.Parse("""
            {"content":[{"type":"text","text":"An error occurred invoking 'get_city_amendments': UF 'XX' inválida."}],"isError":true}
            """).RootElement;

        var shaped = ToolResultShaper.Simplify("get_city_amendments", raw);

        Assert.True(ToolResultShaper.IsError(shaped));
        Assert.Equal("UF 'XX' inválida.", ((JsonElement)shaped!).GetProperty("error").GetString());
    }

    [Fact]
    public void Generic_english_error_is_replaced_by_pt_BR_guidance()
    {
        var raw = JsonDocument.Parse("""
            {"content":[{"type":"text","text":"An error occurred invoking 'get_city_amendments'."}],"isError":true}
            """).RootElement;

        var message = ((JsonElement)ToolResultShaper.Simplify("get_city_amendments", raw)!).GetProperty("error").GetString();

        Assert.StartsWith("Argumentos inválidos para a ferramenta get_city_amendments", message);
    }

    [Fact]
    public void Preview_is_one_compact_line_with_readable_accents()
    {
        var preview = ToolResultShaper.Preview(JsonDocument.Parse("{\"area\": \"Educação\"}").RootElement);

        Assert.Equal("{\"area\":\"Educação\"}", preview);
    }
}
