using PublicData.McpServer.Municipalities;

namespace PublicData.Tests.Server;

public sealed class MunicipalityDirectoryTests
{
    private static readonly MunicipalityDirectory Directory = MunicipalityDirectory.LoadEmbedded();

    [Fact]
    public void Snapshot_has_every_municipality() => Assert.True(Directory.Count >= 5560, $"Count = {Directory.Count}");

    [Theory]
    [InlineData("Campinas", "SP", "51885242000140")]
    [InlineData("campinas", "sp", "51885242000140")]
    [InlineData("Campinas (SP)", "", "51885242000140")]
    [InlineData("Campinas, SP", "", "51885242000140")]
    [InlineData("Recife - PE", "", "10565000000192")]
    [InlineData("Recife", "Pernambuco", "10565000000192")]
    [InlineData("Goiânia", "GO", "01612092000123")]
    [InlineData("goiania", "goias", "01612092000123")]
    // The Transferegov registers Brasília under the Distrito Federal CNPJ, not the one SICONFI lists.
    [InlineData("Brasília", "DF", "00394601000126")]
    public void Finds_municipality_ignoring_accents_case_and_state_format(string city, string state, string expectedCnpj)
    {
        var lookup = Directory.Find(city, state);

        Assert.NotNull(lookup.Municipality);
        Assert.Equal(expectedCnpj, lookup.Municipality.Cnpj);
    }

    [Fact]
    public void State_by_full_name_without_accents_resolves()
    {
        var lookup = Directory.Find("sao jose dos campos", "São Paulo");

        Assert.NotNull(lookup.Municipality);
        Assert.Equal("SP", lookup.Municipality.State);
        Assert.Equal("São José dos Campos", lookup.Municipality.Name);
    }

    [Fact]
    public void Ambiguous_name_without_state_lists_the_options()
    {
        var lookup = Directory.Find("Santa Rita", "");

        Assert.Null(lookup.Municipality);
        Assert.Contains("Santa Rita - MA", lookup.Message);
        Assert.Contains("Santa Rita - PB", lookup.Message);
        Assert.Contains("Informe o estado", lookup.Message);
    }

    [Fact]
    public void Invalid_state_is_refused() =>
        Assert.Equal("UF 'XX' inválida. Use a sigla do estado com 2 letras, por exemplo SP.", Directory.Find("Campinas", "XX").Message);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_city_asks_for_the_name(string? city) =>
        Assert.Equal("Informe o nome do município.", Directory.Find(city, "SP").Message);

    [Fact]
    public void Unknown_city_is_reported_with_suggestions_when_possible()
    {
        Assert.Contains("não encontrado", Directory.Find("Xyzópolis", "").Message);

        var partial = Directory.Find("Campin", "SP");
        Assert.Null(partial.Municipality);
        Assert.Contains("Você quis dizer", partial.Message);
        Assert.Contains("Campinas - SP", partial.Message);
    }
}
