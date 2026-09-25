using System.Diagnostics.CodeAnalysis;

namespace PublicData.McpServer.Municipalities;

public static class StateCodes
{
    private static readonly Dictionary<string, string> ByFoldedName = new(StringComparer.Ordinal)
    {
        ["ACRE"] = "AC", ["ALAGOAS"] = "AL", ["AMAPA"] = "AP", ["AMAZONAS"] = "AM", ["BAHIA"] = "BA",
        ["CEARA"] = "CE", ["DISTRITO FEDERAL"] = "DF", ["ESPIRITO SANTO"] = "ES", ["GOIAS"] = "GO",
        ["MARANHAO"] = "MA", ["MATO GROSSO"] = "MT", ["MATO GROSSO DO SUL"] = "MS", ["MINAS GERAIS"] = "MG",
        ["PARA"] = "PA", ["PARAIBA"] = "PB", ["PARANA"] = "PR", ["PERNAMBUCO"] = "PE", ["PIAUI"] = "PI",
        ["RIO DE JANEIRO"] = "RJ", ["RIO GRANDE DO NORTE"] = "RN", ["RIO GRANDE DO SUL"] = "RS",
        ["RONDONIA"] = "RO", ["RORAIMA"] = "RR", ["SANTA CATARINA"] = "SC", ["SAO PAULO"] = "SP",
        ["SERGIPE"] = "SE", ["TOCANTINS"] = "TO",
    };

    private static readonly HashSet<string> Codes = [.. ByFoldedName.Values];

    /// <summary>"sp", "SP", "São Paulo" and "sao paulo" all become "SP".</summary>
    public static bool TryNormalize(string input, [NotNullWhen(true)] out string? uf)
    {
        var folded = TextFold.Fold(input);
        uf = Codes.Contains(folded) ? folded : ByFoldedName.GetValueOrDefault(folded);
        return uf is not null;
    }
}
