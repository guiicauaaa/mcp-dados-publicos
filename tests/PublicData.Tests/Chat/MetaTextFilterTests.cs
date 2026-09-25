using PublicData.Chat.Core.Chat;

namespace PublicData.Tests.Chat;

public sealed class MetaTextFilterTests
{
    [Fact]
    public void Leading_meta_sentence_is_removed_and_the_rest_kept() =>
        Assert.Equal("Você pode me informar qual é o município?",
            MetaTextFilter.Clean("Não é necessário usar a função \"get_city_amendments\" para responder à sua pergunta. Você pode me informar qual é o município?"));

    [Fact]
    public void Answer_made_only_of_meta_text_becomes_a_neutral_reply() =>
        Assert.Equal(MetaTextFilter.Fallback, MetaTextFilter.Clean("Não é necessário usar a função para responder à sua saudação."));

    [Theory]
    [InlineData("Olá! Como posso ajudar você hoje?")]
    [InlineData("A Lei de Responsabilidade Fiscal (LRF) regula a gestão financeira. Não é necessário ser especialista para entendê-la.")]
    public void Normal_answers_are_untouched(string answer) => Assert.Equal(answer, MetaTextFilter.Clean(answer));
}
