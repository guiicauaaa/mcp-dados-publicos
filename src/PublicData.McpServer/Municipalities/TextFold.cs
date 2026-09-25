using System.Globalization;
using System.Text;

namespace PublicData.McpServer.Municipalities;

public static class TextFold
{
    /// <summary>Upper-case, accent-free, single-spaced key: "São José-dos  Campos" becomes "SAO JOSE DOS CAMPOS".</summary>
    public static string Fold(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(c is '-' or '\'' ? ' ' : char.ToUpperInvariant(c));
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
