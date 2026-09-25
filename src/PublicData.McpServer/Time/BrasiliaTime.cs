namespace PublicData.McpServer.Time;

public static class BrasiliaTime
{
    public static readonly TimeZoneInfo Zone = Resolve();

    public static DateTimeOffset Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            // IANA id: works on Linux and, since .NET 6 with ICU, on Windows too.
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            // Containers without tzdata. Brazil has had no daylight saving time since 2019.
            return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }
}
