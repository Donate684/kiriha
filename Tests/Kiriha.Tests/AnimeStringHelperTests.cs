using Kiriha.Utils.Parsing;

namespace Kiriha.Tests;

public sealed class AnimeStringHelperTests
{
    [Theory]
    [InlineData("Kaguya-sama: Love is War - The First Kiss That Never Ends", "kaguya sama love is war 1st kiss that never ends")]
    [InlineData("Mushoku Tensei II: Isekai Ittara Honki Dasu", "mushoku tensei 2 isekai ittara honki dasu")]
    [InlineData("Fate/Grand Order: Zettai Majuu Sensen Babylonia", "fate grand order zettai majuu sensen babylonia")]
    [InlineData("Oshi no Ko 2nd Season", "oshi no ko 2")]
    public void Normalize_CanonicalizesCommonTitleNoise(string input, string expected)
    {
        Assert.Equal(expected, AnimeStringHelper.Normalize(input));
    }

    [Fact]
    public void Normalize_EmptyOrWhitespaceReturnsEmptyString()
    {
        Assert.Equal(string.Empty, AnimeStringHelper.Normalize("   "));
    }

    [Fact]
    public void CleanShikiDescription_RemovesBbCodeButKeepsText()
    {
        var cleaned = AnimeStringHelper.CleanShikiDescription("[b]Bold[/b] and [url=https://example.test]link[/url]");

        Assert.Equal("Bold and link", cleaned);
    }

    [Fact]
    public void CleanShikiDescription_RemovesBlockedPlaceholder()
    {
        var cleaned = AnimeStringHelper.CleanShikiDescription("Заблокировано по требованию Роскомнадзора");

        Assert.Equal(string.Empty, cleaned);
    }
}
