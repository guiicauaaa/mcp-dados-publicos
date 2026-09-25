using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PublicData.Api.Data;
using PublicData.Tests.Support;

namespace PublicData.Tests.Api;

/// <summary>API end to end: SSE chat with real MCP calls, PostgreSQL persistence and the audit trail.</summary>
public sealed class ApiTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private HttpClient Client => api.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record SseEvent(string Type, JsonElement Data);

    private async Task<List<SseEvent>> ChatAsync(string message, Guid? conversationId = null)
    {
        using var response = await Client.PostAsJsonAsync("/api/chat", new { conversationId, message }, Ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(Ct);
        return [.. body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.Split('\n'))
            .Select(lines => new SseEvent(
                lines.Single(l => l.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..],
                JsonDocument.Parse(lines.Single(l => l.StartsWith("data: ", StringComparison.Ordinal))["data: ".Length..]).RootElement.Clone()))];
    }

    [Fact]
    public async Task Status_reports_model_mcp_tools_and_database()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");

        var status = await Client.GetFromJsonAsync<JsonElement>("/api/status", Ct);

        Assert.Equal("ready", status.GetProperty("database").GetProperty("state").GetString());
        var mcp = status.GetProperty("mcp");
        Assert.Equal("ready", mcp.GetProperty("state").GetString());
        Assert.Equal("stdio", mcp.GetProperty("transport").GetString());
        Assert.Equal("2026-07-28", mcp.GetProperty("protocolVersion").GetString());
        var tools = mcp.GetProperty("tools").EnumerateArray().ToDictionary(t => t.GetProperty("name").GetString()!, t => t.GetProperty("offeredToModel").GetBoolean());
        Assert.True(tools["get_city_amendments"]);
        Assert.False(tools["get_current_datetime"]);
    }

    [Fact]
    public async Task Chat_streams_the_mcp_call_and_persists_the_answer_and_the_audit_trail()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");
        api.Model.Script = new ScriptedChatClient(
            (_, _) => ScriptedChatClient.Call("get_city_amendments", new() { ["city"] = "Campinas", ["state"] = "XX" }),
            (_, _) => ScriptedChatClient.Text("Essa UF não existe. Qual é o estado?"));

        var events = await ChatAsync("Quanto Campinas recebeu de emendas Pix?");

        Assert.Equal(["conversation", "tool_call", "tool_result", "answer", "done"], events.Select(e => e.Type));
        Assert.Equal("Campinas", events[1].Data.GetProperty("arguments").GetProperty("city").GetString());
        Assert.True(events[2].Data.GetProperty("isError").GetBoolean());
        Assert.StartsWith("UF 'XX' inválida", events[2].Data.GetProperty("result").GetProperty("error").GetString());
        Assert.Equal("Essa UF não existe. Qual é o estado?", events[3].Data.GetProperty("text").GetString());

        var conversationId = events[0].Data.GetProperty("conversationId").GetGuid();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messages = await db.Messages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id).ToListAsync(Ct);
        Assert.Equal([MessageRoles.User, MessageRoles.Assistant], messages.Select(m => m.Role));

        var audit = await db.ToolCalls.SingleAsync(t => t.ConversationId == conversationId, Ct);
        Assert.Equal("chat", audit.Origin);
        Assert.Equal("get_city_amendments", audit.ToolName);
        Assert.True(audit.IsError);
        Assert.Equal("public-data-mcp 1.0.0", audit.McpServer);
        Assert.Equal("stdio", audit.McpTransport);
        Assert.Equal(messages[1].Id, audit.MessageId);
        using var arguments = JsonDocument.Parse(audit.ArgumentsJson);
        Assert.Equal("XX", arguments.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Follow_up_sends_only_the_text_of_previous_turns_as_history()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");
        api.Model.Script = new ScriptedChatClient((_, _) => ScriptedChatClient.Text("Primeira resposta."));
        var first = await ChatAsync("Primeira pergunta");
        var conversationId = first[0].Data.GetProperty("conversationId").GetGuid();

        var script = new ScriptedChatClient((_, _) => ScriptedChatClient.Text("Segunda resposta."));
        api.Model.Script = script;
        await ChatAsync("E agora?", conversationId);

        var sent = script.Requests[0].Messages;
        Assert.Equal(["Primeira pergunta", "Primeira resposta.", "E agora?"], sent.Select(m => m.Text));
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.User], sent.Select(m => m.Role));
    }

    [Fact]
    public async Task Empty_question_is_rejected_in_the_stream()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");

        var events = await ChatAsync("   ");

        Assert.Equal(["error", "done"], events.Select(e => e.Type));
    }

    [Fact]
    public async Task Tool_panel_calls_mcp_directly_and_is_audited_as_manual()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");

        using var response = await Client.PostAsJsonAsync("/api/mcp/tools/get_city_amendments/invoke",
            new { arguments = new { city = "Santa Rita", year = 2026 } }, Ct);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("Santa Rita - PB", result.GetProperty("result").GetProperty("error").GetString());

        var page = await Client.GetFromJsonAsync<JsonElement>("/api/audit/tool-calls?origin=manual&status=error", Ct);
        Assert.True(page.GetProperty("total").GetInt32() >= 1);
        var item = page.GetProperty("items")[0];
        Assert.Equal("manual", item.GetProperty("origin").GetString());
        Assert.Equal("Santa Rita", item.GetProperty("arguments").GetProperty("city").GetString());

        var detail = await Client.GetFromJsonAsync<JsonElement>($"/api/audit/tool-calls/{item.GetProperty("id").GetInt64()}", Ct);
        Assert.Contains("Santa Rita - MA", detail.GetProperty("result").GetProperty("error").GetString());

        var summary = await Client.GetFromJsonAsync<JsonElement>("/api/audit/summary", Ct);
        Assert.True(summary.GetProperty("errors").GetInt32() >= 1);
    }

    [Fact]
    public async Task Reconnect_replaces_the_mcp_session_and_the_new_one_stays_up()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");

        using var response = await Client.PostAsync("/api/mcp/reconnect", null, Ct);
        var status = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("ready", status.GetProperty("state").GetString());

        // The old session ending must not drop the new one (the gateway watches Completion of each session).
        await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        using var call = await Client.PostAsJsonAsync("/api/mcp/tools/get_current_datetime/invoke", new { arguments = new { } }, Ct);
        var result = await call.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.False(result.GetProperty("isError").GetBoolean());
        var after = await Client.GetFromJsonAsync<JsonElement>("/api/status", Ct);
        Assert.Equal("ready", after.GetProperty("mcp").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Unknown_tool_is_not_found()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");

        using var response = await Client.PostAsJsonAsync("/api/mcp/tools/drop_database/invoke", new { arguments = new { } }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Conversations_are_listed_read_and_deleted_keeping_the_audit_trail()
    {
        Assert.SkipWhen(ApiFactory.SkipReason is not null, ApiFactory.SkipReason ?? "");
        api.Model.Script = new ScriptedChatClient(
            (_, _) => ScriptedChatClient.Call("get_city_amendments", new() { ["city"] = "Xyzópolis" }),
            (_, _) => ScriptedChatClient.Text("Não encontrei esse município."));
        var events = await ChatAsync("Quanto Xyzópolis recebeu?");
        var id = events[0].Data.GetProperty("conversationId").GetGuid();

        var list = await Client.GetFromJsonAsync<JsonElement>("/api/conversations", Ct);
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id && c.GetProperty("messageCount").GetInt32() == 2);

        var conversation = await Client.GetFromJsonAsync<JsonElement>($"/api/conversations/{id}", Ct);
        var answer = conversation.GetProperty("messages")[1];
        Assert.Equal("assistant", answer.GetProperty("role").GetString());
        Assert.Equal("get_city_amendments", answer.GetProperty("toolCalls")[0].GetProperty("toolName").GetString());

        using var deleted = await Client.DeleteAsync($"/api/conversations/{id}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await Client.GetAsync($"/api/conversations/{id}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var kept = await db.ToolCalls.SingleAsync(t => t.ConversationId == id, Ct);
        Assert.Null(kept.MessageId);
    }
}
