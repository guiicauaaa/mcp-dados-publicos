using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using PublicData.McpServer.Municipalities;
using PublicData.McpServer.Tools;
using PublicData.McpServer.Transferegov;
using PublicData.Tests.Support;

namespace PublicData.Tests.Server;

public sealed class AmendmentToolsTests
{
    private static readonly MunicipalityDirectory Directory = MunicipalityDirectory.LoadEmbedded();

    private static AmendmentTools Create(FakeHttpHandler handler) => new(
        Directory,
        new TransferegovClient(new HttpClient(handler) { BaseAddress = TransferegovClient.BaseAddress }),
        new MemoryCache(new MemoryCacheOptions()),
        FixedTimeProvider.Sept25,
        NullLogger<AmendmentTools>.Instance);

    [Fact]
    public async Task Year_zero_means_the_current_year_in_Brasilia()
    {
        var handler = FakeHttpHandler.Fixture("transferegov_campinas_2026.json");

        var summary = await Create(handler).GetCityAmendments("Campinas", "SP", 0, TestContext.Current.CancellationToken);

        Assert.Equal(2026, summary.Year);
        Assert.Contains("ano_plano_acao=eq.2026", Assert.Single(handler.Requests).ToString());
        Assert.Equal("25/09/2026 12:53", summary.QueriedAt);
    }

    [Theory]
    [InlineData(2015)]
    [InlineData(2027)]
    public async Task Year_out_of_range_is_refused_before_any_request(int year)
    {
        var handler = FakeHttpHandler.Json("[]");

        var error = await Assert.ThrowsAsync<McpException>(() =>
            Create(handler).GetCityAmendments("Campinas", "SP", year, TestContext.Current.CancellationToken));

        Assert.Contains("fora do intervalo", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ambiguous_city_is_refused_with_the_options()
    {
        var error = await Assert.ThrowsAsync<McpException>(() =>
            Create(FakeHttpHandler.Json("[]")).GetCityAmendments("Santa Rita", "", 2026, TestContext.Current.CancellationToken));

        Assert.Contains("Santa Rita - PB", error.Message);
    }

    [Fact]
    public async Task Network_failure_becomes_a_friendly_message()
    {
        var handler = new FakeHttpHandler((_, _, _) => throw new HttpRequestException("Este host não é conhecido."));

        var error = await Assert.ThrowsAsync<McpException>(() =>
            Create(handler).GetCityAmendments("Campinas", "SP", 2026, TestContext.Current.CancellationToken));

        Assert.Equal("A API do Transferegov não respondeu agora. Tente de novo em alguns instantes.", error.Message);
    }

    [Fact]
    public async Task Repeated_question_comes_from_the_cache()
    {
        var handler = FakeHttpHandler.Fixture("transferegov_campinas_2026.json");
        var tools = Create(handler);

        var first = await tools.GetCityAmendments("Campinas", "SP", 2026, TestContext.Current.CancellationToken);
        var second = await tools.GetCityAmendments("campinas", "São Paulo", 2026, TestContext.Current.CancellationToken);

        Assert.Equal(first, second with { ByArea = first.ByArea, ByParliamentarian = first.ByParliamentarian });
        Assert.Equal(1, handler.Calls);
    }
}
