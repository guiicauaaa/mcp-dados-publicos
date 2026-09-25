using PublicData.McpServer.Formatting;

namespace PublicData.Tests.Server;

public sealed class BrlTests
{
    [Theory]
    [InlineData("0", "R$ 0,00")]
    [InlineData("398000", "R$ 398.000,00")]
    [InlineData("999994.99", "R$ 999.994,99")]
    [InlineData("1000000", "R$ 1,00 milhão")]
    [InlineData("1990000", "R$ 1,99 milhão")]
    [InlineData("1995000", "R$ 2,00 milhões")]
    [InlineData("7910250", "R$ 7,91 milhões")]
    [InlineData("12501712.08", "R$ 12,50 milhões")]
    [InlineData("999999999", "R$ 1,00 bilhão")]
    [InlineData("1200000000", "R$ 1,20 bilhão")]
    [InlineData("11084425866.54", "R$ 11,08 bilhões")]
    public void Short_uses_pt_BR_format_with_singular_and_plural(string value, string expected) =>
        Assert.Equal(expected, Brl.Short(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Never_uses_non_breaking_space() => Assert.DoesNotContain(' ', Brl.Short(398_000m) + Brl.Short(7_910_000m));
}
