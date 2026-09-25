namespace PublicData.Tests.Support;

public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    /// <summary>Friday, 25/09/2026 12:53 in Brasília (UTC-3).</summary>
    public static readonly FixedTimeProvider Sept25 = new(new DateTimeOffset(2026, 9, 25, 15, 53, 0, TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => utcNow;
}
