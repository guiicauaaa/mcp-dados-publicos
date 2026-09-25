using PublicData.Api.Services;

namespace PublicData.Tests.Api;

public sealed class ChatServiceTests
{
    [Fact]
    public void Title_is_cut_on_a_character_boundary()
    {
        // An emoji is two UTF-16 chars; cutting between them leaves a lone surrogate that PostgreSQL rejects.
        var question = new string('a', 78) + "😀" + " resto da pergunta";

        var title = ChatService.Truncate(question, 80);

        Assert.Equal(new string('a', 78) + "…", title);
        Assert.False(char.IsHighSurrogate(title[^2]));
    }

    [Fact]
    public void Short_title_is_kept() => Assert.Equal("Quanto Campinas recebeu?", ChatService.Truncate("Quanto Campinas recebeu?", 80));
}
