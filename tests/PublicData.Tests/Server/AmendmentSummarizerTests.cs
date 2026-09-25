using System.Text.Json;
using PublicData.McpServer.Municipalities;
using PublicData.McpServer.Tools;
using PublicData.McpServer.Transferegov;
using PublicData.Tests.Support;

namespace PublicData.Tests.Server;

public sealed class AmendmentSummarizerTests
{
    private static readonly Municipality Campinas = new(3509502, "Campinas", "SP", "51885242000140");
    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 25, 12, 36, 0, TimeSpan.FromHours(-3));

    private static List<ActionPlan> Load(string fixture) =>
        JsonSerializer.Deserialize<List<ActionPlan>>(Fixtures.Read(fixture))!;

    [Fact]
    public void Campinas_2026_real_response()
    {
        var summary = AmendmentSummarizer.Summarize(Campinas, 2026, Load("transferegov_campinas_2026.json"), FetchedAt);

        Assert.Equal("Campinas - SP", summary.City);
        Assert.Equal("R$ 7,91 milhões", summary.TotalIndicated);
        Assert.Equal(5, summary.Amendments);
        Assert.Equal(3, summary.BlockedAmendments);
        Assert.Equal("Jonas Donizette", summary.ByParliamentarian[0].Name);
        Assert.Equal("R$ 5,52 milhões", summary.ByParliamentarian[0].Amount);
        Assert.Equal("Educação", summary.ByArea[0].Name);
        Assert.Equal("25/09/2026 12:36", summary.QueriedAt);
        Assert.Equal(AmendmentSummarizer.Source, summary.Source);
    }

    [Fact]
    public void Campinas_2025_excludes_blocked_plans_that_would_count_twice()
    {
        var plans = Load("transferegov_campinas_2025.json");
        var summary = AmendmentSummarizer.Summarize(Campinas, 2025, plans, FetchedAt);

        Assert.Equal(23, plans.Count);
        Assert.Equal("R$ 12,50 milhões", summary.TotalIndicated);
        Assert.Equal(14, summary.Amendments);
        Assert.Equal(9, summary.BlockedAmendments);
        Assert.True(summary.ByParliamentarian.Count <= 5);
    }

    [Fact]
    public void No_plans_is_a_normal_result()
    {
        var summary = AmendmentSummarizer.Summarize(Campinas, 2021, [], FetchedAt);

        Assert.Equal("R$ 0,00", summary.TotalIndicated);
        Assert.Empty(summary.ByParliamentarian);
        Assert.StartsWith("Consulta concluída: nenhum plano", summary.Note);
    }

    [Fact]
    public void Only_blocked_plans_is_a_completed_query_not_an_error()
    {
        // Goiânia 2026 on 25/09/2026: a single plan, blocked.
        var summary = AmendmentSummarizer.Summarize(Campinas, 2026, [new ActionPlan("IMPEDIDO", "X", "12-Educação", 100, 0)], FetchedAt);

        Assert.Equal("R$ 0,00", summary.TotalIndicated);
        Assert.Equal(1, summary.BlockedAmendments);
        Assert.StartsWith("Consulta concluída: nenhuma emenda Pix válida", summary.Note);
    }

    [Fact]
    public void Serialized_result_is_compact_readable_and_has_no_bank_data()
    {
        var summary = AmendmentSummarizer.Summarize(Campinas, 2026, Load("transferegov_campinas_2026.json"), FetchedAt);
        var json = JsonSerializer.Serialize(summary, ToolJson.Options);

        Assert.Contains("\"total_indicated\":\"R$ 7,91 milhões\"", json);
        Assert.Contains("\"sent_by_parliamentarian\":[{\"name\":\"Jonas Donizette\"", json);
        Assert.Contains("Educação", json); // not escaped as ção
        Assert.DoesNotContain("banco", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("agencia", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conta", json, StringComparison.OrdinalIgnoreCase);
        Assert.True(json.Length < 1500, $"{json.Length} chars");
    }

    [Theory]
    [InlineData("12-Educação / 361-Ensino Fundamental , 12-Educação / 365-Educação Infantil", "Educação")]
    [InlineData("23-Comércio e Serviços / 695-Turismo", "Comércio e Serviços")]
    [InlineData(null, "Não informada")]
    public void Main_area_is_the_first_policy_area(string? areas, string expected) =>
        Assert.Equal(expected, new ActionPlan("CIENTE", "X", areas, 0, 0).MainArea);
}
