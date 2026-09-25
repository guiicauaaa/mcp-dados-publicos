using System.Text.Json.Serialization;

namespace PublicData.McpServer.Transferegov;

/// <summary>What the model receives: small (~0.6 KB), already formatted in pt-BR, with the source.</summary>
public sealed record AmendmentSummary(
    [property: JsonPropertyName("city")] string City,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("total_indicated")] string TotalIndicated,
    [property: JsonPropertyName("amendments")] int Amendments,
    [property: JsonPropertyName("blocked_amendments")] int BlockedAmendments,
    // "sent_by": measured, with "by_parliamentarian" llama3.2 wrote "the congress members who RECEIVED the amendments".
    [property: JsonPropertyName("sent_by_parliamentarian")] IReadOnlyList<AmountByName> ByParliamentarian,
    [property: JsonPropertyName("by_policy_area")] IReadOnlyList<AmountByName> ByArea,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("queried_at")] string QueriedAt);

public sealed record AmountByName(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("amendments")] int Amendments);
