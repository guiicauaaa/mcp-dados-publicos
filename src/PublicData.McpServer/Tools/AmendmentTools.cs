using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PublicData.McpServer.Municipalities;
using PublicData.McpServer.Time;
using PublicData.McpServer.Transferegov;

namespace PublicData.McpServer.Tools;

[McpServerToolType]
public sealed class AmendmentTools(
    MunicipalityDirectory directory,
    TransferegovClient transferegov,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<AmendmentTools> logger)
{
    public const string ToolName = "get_city_amendments";
    public const int FirstYear = 2020;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    // The description is in English with "Use ONLY when…": the llama3.2 tool template is English, and the
    // answer language comes from the system prompt. The Title (pt-BR) is for humans in the UI.
    [McpServerTool(Name = ToolName, Title = "Emendas Pix de um município",
        ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = false)]
    [Description("Gets the 'emendas Pix' (special-transfer parliamentary amendments) sent to one Brazilian municipality in one year: total amount, which congress members sent them and for which policy areas. Official federal data from Transferegov. Use ONLY when the user asks about emendas, amendments or money sent by deputies or senators to a city.")]
    public async Task<AmendmentSummary> GetCityAmendments(
        [Description("Municipality name, for example Campinas")] string city,
        [Description("Two-letter state code (UF), for example SP. Leave empty if unknown.")] string state = "",
        [Description("Four-digit year, for example 2026. Omit to use the current year.")] int year = 0,
        CancellationToken cancellationToken = default)
    {
        var now = BrasiliaTime.Now(clock);
        var targetYear = year == 0 ? now.Year : year;
        if (targetYear < FirstYear || targetYear > now.Year)
        {
            throw Refuse($"Ano {targetYear} fora do intervalo: há emendas Pix de {FirstYear} até {now.Year}.");
        }

        // Validation needs no network: bad arguments come back in milliseconds with a message the model can act on.
        var lookup = directory.Find(city, state);
        if (lookup.Municipality is not { } municipality)
        {
            throw Refuse(lookup.Message);
        }

        var cacheKey = $"amend:{municipality.Cnpj}:{targetYear}";
        if (cache.TryGetValue(cacheKey, out CachedPlans? cached) && cached is not null)
        {
            logger.LogInformation("Transferegov: {City}-{State} em {Year} veio do cache ({Count} planos)",
                municipality.Name, municipality.State, targetYear, cached.Plans.Count);
            return AmendmentSummarizer.Summarize(municipality, targetYear, cached.Plans, cached.FetchedAt);
        }

        logger.LogInformation("Transferegov: consultando {City}-{State} (CNPJ {Cnpj}) em {Year}",
            municipality.Name, municipality.State, municipality.Cnpj, targetYear);
        var stopwatch = Stopwatch.StartNew();

        IReadOnlyList<ActionPlan> plans;
        try
        {
            plans = await transferegov.GetActionPlansAsync(municipality.Cnpj, targetYear, cancellationToken);
        }
        catch (Exception ex) when (ex is not McpException && !cancellationToken.IsCancellationRequested)
        {
            // Only McpException messages reach the client; anything else becomes a generic English error.
            logger.LogWarning("Transferegov falhou em {Ms} ms: {Error}", stopwatch.ElapsedMilliseconds, ex.Message);
            throw new McpException("A API do Transferegov não respondeu agora. Tente de novo em alguns instantes.");
        }

        logger.LogInformation("Transferegov: {Count} planos de ação em {Ms} ms", plans.Count, stopwatch.ElapsedMilliseconds);
        cache.Set(cacheKey, new CachedPlans(plans, now), CacheDuration);
        return AmendmentSummarizer.Summarize(municipality, targetYear, plans, now);
    }

    /// <summary>McpException messages reach the client as isError results, which the model reads and explains.</summary>
    private McpException Refuse(string message)
    {
        logger.LogInformation("{Tool} recusado: {Message}", ToolName, message);
        return new McpException(message);
    }

    private sealed record CachedPlans(IReadOnlyList<ActionPlan> Plans, DateTimeOffset FetchedAt);
}
