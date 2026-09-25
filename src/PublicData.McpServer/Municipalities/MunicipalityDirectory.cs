using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PublicData.McpServer.Municipalities;

/// <summary>
/// Snapshot of the 5,570 Brazilian municipalities (SICONFI /entes: IBGE code, name, UF, CNPJ), embedded in the
/// assembly. Resolving a city costs no network call; /entes takes ~36 s cold, so it is not queried at runtime.
/// Regenerate with scripts/update-municipios.ps1.
/// </summary>
public sealed partial class MunicipalityDirectory
{
    private const int MaxCandidates = 5;

    private readonly IReadOnlyList<Municipality> _all;
    private readonly ILookup<string, Municipality> _byName;

    public MunicipalityDirectory(IReadOnlyList<Municipality> all)
    {
        _all = all;
        _byName = all.ToLookup(m => TextFold.Fold(m.Name));
    }

    public int Count => _all.Count;

    public static MunicipalityDirectory LoadEmbedded()
    {
        using var stream = typeof(MunicipalityDirectory).Assembly.GetManifestResourceStream("municipios.csv")
            ?? throw new InvalidOperationException("Recurso municipios.csv não encontrado no assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var list = new List<Municipality>(capacity: 5600);
        reader.ReadLine(); // header: ibge;name;uf;sphere;cnpj;population;capital
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(';');
            if (fields.Length >= 5 && fields[3].Trim() == "M")
            {
                list.Add(new Municipality(
                    int.Parse(fields[0], CultureInfo.InvariantCulture),
                    fields[1].Trim(),
                    fields[2].Trim(),
                    fields[4].Trim()));
            }
        }

        return new MunicipalityDirectory(list);
    }

    /// <summary>
    /// Tolerant lookup: ignores accents and case, accepts the UF by code or by name ("São Paulo"),
    /// and accepts it inside the city argument ("Campinas - SP", "Campinas, SP", "Campinas (SP)"),
    /// which small models often send.
    /// </summary>
    public MunicipalityLookup Find(string? city, string? state)
    {
        city = (city ?? "").Trim();
        state = (state ?? "").Trim();

        if (CitySuffix().Match(city) is { Success: true } match && StateCodes.TryNormalize(match.Groups[2].Value, out var suffixUf))
        {
            city = match.Groups[1].Value.Trim();
            if (state.Length == 0)
            {
                state = suffixUf;
            }
        }

        string? uf = null;
        if (state.Length > 0 && !StateCodes.TryNormalize(state, out uf))
        {
            return MunicipalityLookup.Failed($"UF '{state}' inválida. Use a sigla do estado com 2 letras, por exemplo SP.");
        }

        var key = TextFold.Fold(city);
        if (key.Length == 0)
        {
            return MunicipalityLookup.Failed("Informe o nome do município.");
        }

        var matches = _byName[key].Where(m => uf is null || m.State == uf).ToList();
        if (matches.Count == 1)
        {
            return MunicipalityLookup.Found(matches[0]);
        }

        if (matches.Count > 1)
        {
            var options = string.Join(", ", matches.OrderBy(m => m.State, StringComparer.Ordinal).Select(m => $"{m.Name} - {m.State}"));
            // "o estado": with "Informe a UF" the model expanded UF as "União Federal".
            return MunicipalityLookup.Failed($"Há mais de um município chamado {city}: {options}. Informe o estado.");
        }

        var candidates = _all
            .Where(m => (uf is null || m.State == uf) && TextFold.Fold(m.Name).Contains(key, StringComparison.Ordinal))
            .Take(MaxCandidates)
            .Select(m => $"{m.Name} - {m.State}")
            .ToList();
        var where = uf is null ? "" : $" em {uf}";

        return MunicipalityLookup.Failed(candidates.Count > 0
            ? $"Município '{city}' não encontrado{where}. Você quis dizer: {string.Join(", ", candidates)}?"
            : $"Município '{city}' não encontrado{where}. Verifique o nome e a UF.");
    }

    [GeneratedRegex(@"^(.+?)\s*(?:-|,|/|\()\s*([A-Za-z]{2})\)?$")]
    private static partial Regex CitySuffix();
}
