using PublicData.McpServer.Tools;
using PublicData.Tests.Support;

namespace PublicData.Tests.Server;

public sealed class DateTimeToolsTests
{
    [Fact]
    public void Returns_Brasilia_time_in_pt_BR()
    {
        var now = new DateTimeTools(FixedTimeProvider.Sept25).GetCurrentDateTime();

        Assert.Equal("2026-09-25T12:53:00-03:00", now.Now);
        Assert.Equal("25/09/2026", now.Date);
        Assert.Equal("12:53", now.Time);
        Assert.Equal("sexta-feira", now.Weekday);
    }
}
