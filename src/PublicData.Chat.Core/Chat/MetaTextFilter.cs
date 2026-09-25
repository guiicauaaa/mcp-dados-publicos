using System.Text.RegularExpressions;

namespace PublicData.Chat.Core.Chat;

/// <summary>
/// Cosmetic, deterministic clean-up for turns WITHOUT tool calls. The derived template tells llama3.2 to decide
/// whether a function is needed, and in farewells it sometimes says so out loud ("Não é necessário usar a função
/// get_city_amendments…"). Measured: a template variant forbidding it did not help, so the client drops that
/// sentence. Answers of turns that used tools are never touched.
/// </summary>
public static partial class MetaTextFilter
{
    public const string Fallback = "Tudo bem! Quando quiser, pergunte sobre as emendas Pix de um município.";

    public static string Clean(string answer)
    {
        var cleaned = LeadingMetaSentence().Replace(answer, "").Trim();
        if (cleaned.Length == answer.Trim().Length)
        {
            return answer;
        }

        return cleaned.Length == 0 ? Fallback : cleaned;
    }

    [GeneratedRegex(@"^\s*(?:Não|Nao) (?:é|e) necessári[oa] (?:usar|utilizar|chamar) (?:a|nenhuma|uma|qualquer)? ?(?:fun[cç][aã]o|ferramenta)[^.!?\n]*[.!?]\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingMetaSentence();
}
