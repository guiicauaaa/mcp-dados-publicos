using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PublicData.McpServer.Hosting;
using PublicData.McpServer.Transferegov;
using PublicData.Tests.Support;

namespace PublicData.Tests.Server;

public sealed class TransferegovClientTests
{
    // Short timeouts so the resilience tests run in milliseconds.
    private static readonly TransferegovTimeouts Fast =
        new(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(900), MaxRetries: 2, TimeSpan.FromMilliseconds(10));

    private static TransferegovClient Create(FakeHttpHandler handler)
    {
        var services = new ServiceCollection();
        services.AddTransferegovClient(Fast).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<TransferegovClient>();
    }

    [Fact]
    public async Task Filters_by_cnpj_and_year_and_never_selects_bank_columns()
    {
        var handler = FakeHttpHandler.Fixture("transferegov_campinas_2026.json");

        var plans = await Create(handler).GetActionPlansAsync("51885242000140", 2026, TestContext.Current.CancellationToken);

        Assert.Equal(8, plans.Count);
        var url = Uri.UnescapeDataString(Assert.Single(handler.Requests).ToString());
        Assert.StartsWith(TransferegovClient.BaseAddress + "plano_acao_especial?", url);
        Assert.Contains("cnpj_beneficiario_plano_acao=eq.51885242000140", url);
        Assert.Contains("ano_plano_acao=eq.2026", url);
        Assert.DoesNotContain("banco", url);
        Assert.DoesNotContain("conta", url);
    }

    [Fact]
    public async Task Retries_transient_failures()
    {
        var handler = new FakeHttpHandler((_, attempt, _) => Task.FromResult(attempt < 3
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : FakeHttpHandler.JsonResponse("[]")));

        var plans = await Create(handler).GetActionPlansAsync("51885242000140", 2026, TestContext.Current.CancellationToken);

        Assert.Empty(plans);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Gives_up_when_the_api_is_too_slow()
    {
        var handler = new FakeHttpHandler(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return FakeHttpHandler.JsonResponse("[]");
        });

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Create(handler).GetActionPlansAsync("51885242000140", 2026, TestContext.Current.CancellationToken));
    }
}
