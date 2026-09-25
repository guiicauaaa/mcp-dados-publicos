using System.Globalization;

namespace PublicData.McpServer.Formatting;

/// <summary>
/// Brazilian real formatting for the model: a 3B model copies "R$ 7,91 milhões" far more reliably
/// than it formats 7910000.00 by itself.
/// </summary>
public static class Brl
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>"R$ 398.000,00". Built from N2 instead of C2: the pt-BR currency format uses a non-breaking space.</summary>
    public static string Format(decimal value) => "R$ " + value.ToString("N2", PtBr);

    /// <summary>"R$ 7,91 milhões", "R$ 1,99 milhão", "R$ 11,08 bilhões"; below one million, the full value.</summary>
    public static string Short(decimal value)
    {
        var abs = Math.Abs(value);
        if (abs < 1_000_000m)
        {
            return Format(value); // never round R$ 999.994,99 up to "1,00 milhão"
        }

        var millions = Math.Round(abs / 1_000_000m, 2, MidpointRounding.AwayFromZero);
        if (abs < 1_000_000_000m && millions < 1000m)
        {
            return Scaled(value, millions, "milhão", "milhões");
        }

        var billions = Math.Round(abs / 1_000_000_000m, 2, MidpointRounding.AwayFromZero);
        return Scaled(value, billions, "bilhão", "bilhões");
    }

    // Singular only when the integer part is 1: "1,99 milhão" but "2,00 milhões".
    private static string Scaled(decimal original, decimal scaled, string singular, string plural) =>
        (original < 0 ? "-" : "") + "R$ " + scaled.ToString("N2", PtBr) + " " + (scaled < 2m ? singular : plural);
}
