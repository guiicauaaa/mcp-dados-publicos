namespace PublicData.Api.Services;

public static class ComponentState
{
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Unavailable = "unavailable";
}

public sealed record ModelStatus(
    string State,
    string BaseUrl,
    string RequestedModel,
    string? EffectiveModel,
    string? OllamaVersion,
    bool DerivedModelCreated,
    string? Message);

public sealed record McpToolInfo(string Name, string? Title, string? Description, object InputSchema, bool OfferedToModel);

public sealed record McpStatus(
    string State,
    string Transport,
    string? Endpoint,
    string? ServerName,
    string? ServerVersion,
    string? ProtocolVersion,
    IReadOnlyList<McpToolInfo> Tools,
    string? Message);

/// <param name="ServerOwn">True for the server's own lines (Transferegov queries, refusals); false for SDK/hosting noise.</param>
public sealed record ServerLogLine(long Sequence, DateTimeOffset At, string? Level, string? Category, string Message, bool ServerOwn);
