using System.Net.Http.Json;

namespace PublicData.McpServer.Transferegov;

/// <summary>
/// Transferegov "transferências especiais" API (PostgREST, no key). One GET per question.
/// </summary>
public sealed class TransferegovClient(HttpClient http)
{
    public static readonly Uri BaseAddress = new("https://api.transferegov.gestao.gov.br/transferenciasespeciais/");

    // Explicit column list: the full row also carries the beneficiary's bank, branch and account (LGPD).
    internal const string Columns =
        "situacao_plano_acao,nome_parlamentar_emenda_plano_acao,codigo_descricao_areas_politicas_publicas_plano_acao," +
        "valor_custeio_plano_acao,valor_investimento_plano_acao";

    // PostgREST returns at most 1000 rows per request; a single municipality per year stays far below that.
    internal const int PageSize = 1000;

    /// <summary>Plans filtered by the municipality CNPJ: the beneficiary name is accent-sensitive and inconsistent.</summary>
    public async Task<IReadOnlyList<ActionPlan>> GetActionPlansAsync(string cnpj, int year, CancellationToken cancellationToken)
    {
        var url = $"plano_acao_especial?select={Columns}&cnpj_beneficiario_plano_acao=eq.{cnpj}&ano_plano_acao=eq.{year}&limit={PageSize}";
        return await http.GetFromJsonAsync<List<ActionPlan>>(url, cancellationToken) ?? [];
    }
}
