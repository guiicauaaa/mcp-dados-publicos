namespace PublicData.McpServer.Municipalities;

public sealed record Municipality(int IbgeCode, string Name, string State, string Cnpj);

/// <summary>Either a municipality or a pt-BR message that tells the user (and the model) what to fix.</summary>
public sealed record MunicipalityLookup(Municipality? Municipality, string Message)
{
    public static MunicipalityLookup Found(Municipality municipality) => new(municipality, "");

    public static MunicipalityLookup Failed(string message) => new(null, message);
}
