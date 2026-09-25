using System.Globalization;
using PublicData.McpServer.Formatting;
using PublicData.McpServer.Municipalities;

namespace PublicData.McpServer.Transferegov;

/// <summary>Pure aggregation: raw plans in, compact pt-BR summary out.</summary>
public static class AmendmentSummarizer
{
    // "valor indicado" in the source itself: the Fonte line is printed by the client on every answer with data,
    // so the indicated-vs-paid distinction does not depend on the model's wording (it tends to mirror "recebeu").
    public const string Source = "Transferegov - Transferências Especiais (emendas Pix), governo federal; valor indicado nos planos de ação";
    private const int TopCount = 5;

    public static AmendmentSummary Summarize(Municipality city, int year, IReadOnlyList<ActionPlan> plans, DateTimeOffset fetchedAt)
    {
        var valid = plans.Where(p => !p.IsBlocked).ToList();
        var blocked = plans.Count - valid.Count;

        // "Consulta concluída": measured, a zero total without it was read by the model as a tool error.
        var note = (plans.Count, valid.Count) switch
        {
            (0, _) => "Consulta concluída: nenhum plano de ação de emenda Pix para este município neste ano.",
            (_, 0) => $"Consulta concluída: nenhuma emenda Pix válida neste ano; {blocked} plano(s) impedido(s) não entram no total.",
            _ => "Consulta concluída. Valor indicado nos planos de ação (custeio + investimento), enviado pelos parlamentares ao município. Planos impedidos não entram no total.",
        };
        if (plans.Count >= TransferegovClient.PageSize)
        {
            note += " Resultado limitado a 1.000 planos.";
        }

        return new AmendmentSummary(
            $"{city.Name} - {city.State}",
            year,
            Brl.Short(valid.Sum(p => p.Total)),
            valid.Count,
            blocked,
            Top(valid.GroupBy(p => string.IsNullOrWhiteSpace(p.Parliamentarian) ? "Não informado" : p.Parliamentarian.Trim())),
            Top(valid.GroupBy(p => p.MainArea)),
            Source,
            note,
            fetchedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
    }

    private static List<AmountByName> Top(IEnumerable<IGrouping<string, ActionPlan>> groups) => groups
        .Select(g => (Name: g.Key, Total: g.Sum(p => p.Total), Count: g.Count()))
        .OrderByDescending(x => x.Total)
        .ThenBy(x => x.Name, StringComparer.Ordinal)
        .Take(TopCount)
        .Select(x => new AmountByName(x.Name, Brl.Short(x.Total), x.Count))
        .ToList();
}
