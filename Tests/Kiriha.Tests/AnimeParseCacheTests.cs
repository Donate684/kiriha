using Kiriha.Utils;

namespace Kiriha.Tests;

public sealed class AnimeParseCacheTests
{
    [Fact]
    public void Parse_BlankFilenameReturnsEmptyList()
    {
        Assert.Empty(AnimeParseCache.Parse(""));
    }

    [Theory]
    [InlineData("[SubsPlease] Sousou no Frieren - 12 (1080p)", "Sousou no Frieren", "12")]
    [InlineData("[Erai-raws] Oshi no Ko - S02E03 [1080p]", "Oshi no Ko", "03")]
    [InlineData("Fullmetal Alchemist Brotherhood - 01", "Fullmetal Alchemist Brotherhood", "01")]
    public void Parse_ExtractsTitleAndEpisodeFromCommonReleaseNames(
        string filename,
        string expectedTitle,
        string expectedEpisode)
    {
        var parsed = AnimeParseCache.Parse(filename);

        var title = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
        var episode = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedEpisode, episode);
    }
}
