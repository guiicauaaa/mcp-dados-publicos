namespace PublicData.McpServer.Transferegov;

/// <summary>Resilience settings for the Transferegov API (measured: 0.1–0.9 s warm, ~2 s on the first call).</summary>
public sealed record TransferegovTimeouts(TimeSpan Attempt, TimeSpan Total, int MaxRetries, TimeSpan RetryDelay)
{
    public static readonly TransferegovTimeouts Default =
        new(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(20), MaxRetries: 2, TimeSpan.FromMilliseconds(500));
}
