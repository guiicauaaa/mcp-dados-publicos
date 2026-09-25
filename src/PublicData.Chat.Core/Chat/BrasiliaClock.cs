namespace PublicData.Chat.Core.Chat;

public static class BrasiliaClock
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateTimeOffset Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }
}
