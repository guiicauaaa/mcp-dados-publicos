using System.Text.Json.Serialization;

namespace PublicData.McpServer.Transferegov;

/// <summary>One "plano de ação" of a special transfer, with only the columns the tool needs.</summary>
public sealed record ActionPlan(
    [property: JsonPropertyName("situacao_plano_acao")] string? Status,
    [property: JsonPropertyName("nome_parlamentar_emenda_plano_acao")] string? Parliamentarian,
    [property: JsonPropertyName("codigo_descricao_areas_politicas_publicas_plano_acao")] string? Areas,
    [property: JsonPropertyName("valor_custeio_plano_acao")] decimal Costing,
    [property: JsonPropertyName("valor_investimento_plano_acao")] decimal Investment)
{
    public decimal Total => Costing + Investment;

    /// <summary>A blocked plan is usually re-issued with the same amount; counting both would double it.</summary>
    public bool IsBlocked => Status?.StartsWith("IMPEDIDO", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>"12-Educação / 361-Ensino Fundamental , 10-Saúde / …" becomes "Educação".</summary>
    public string MainArea
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Areas))
            {
                return "Não informada";
            }

            var first = Areas.Split(',')[0].Split('/')[0].Trim();
            var dash = first.IndexOf('-');
            return dash >= 0 ? first[(dash + 1)..].Trim() : first;
        }
    }
}
