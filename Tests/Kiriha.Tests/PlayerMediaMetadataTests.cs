using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services;

namespace Kiriha.Tests;

public sealed class PlayerMediaMetadataTests
{
    [Fact]
    public void FromVideoPath_UsesFileNameWithoutExtensionAsFallbackTitle()
    {
        var metadata = PlayerMediaMetadata.FromVideoPath(@"C:\Anime\Cowboy Bebop - 01.mkv");

        Assert.Equal("Cowboy Bebop - 01", metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleEn);
        Assert.Equal(string.Empty, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Fact]
    public void FromVideoPath_BlankPathReturnsEmptyMetadata()
    {
        var metadata = PlayerMediaMetadata.FromVideoPath(" ");

        Assert.Equal(string.Empty, metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleEn);
        Assert.Equal(string.Empty, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Theory]
    [InlineData(new[] { "--player", PlayerProcessBridge.ResidentArg }, true)]
    [InlineData(new[] { "--PLAYER", "--PLAYER-RESIDENT" }, true)]
    [InlineData(new[] { "--player" }, false)]
    public void PlayerProcessBridge_IsResident_IsCaseInsensitive(string[] args, bool expected)
    {
        Assert.Equal(expected, PlayerProcessBridge.IsResident(args));
    }

    [Fact]
    public void FilenameResolver_ReturnsFallbackForBlankPath()
    {
        var resolver = new FilenamePlayerMediaMetadataResolver();

        var metadata = resolver.Resolve("");

        Assert.Equal(string.Empty, metadata.TitleRu);
        Assert.Equal(string.Empty, metadata.TitleEn);
        Assert.Equal(string.Empty, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }

    [Theory]
    [InlineData(@"C:\Anime\[SubsPlease] Sousou no Frieren - 12 (1080p).mkv", "Sousou no Frieren", "12")]
    [InlineData(@"C:\Anime\[Erai-raws] Oshi no Ko - S02E03 [1080p].mkv", "Oshi no Ko", "03")]
    [InlineData(@"C:\Anime\Fullmetal Alchemist Brotherhood - 01.mkv", "Fullmetal Alchemist Brotherhood", "01")]
    public void FilenameResolver_ExtractsTitleAndEpisodeFromCommonReleaseNames(
        string path,
        string expectedTitle,
        string expectedEpisode)
    {
        var resolver = new FilenamePlayerMediaMetadataResolver();

        var metadata = resolver.Resolve(path);

        Assert.Equal(expectedTitle, metadata.TitleRu);
        Assert.Equal(expectedEpisode, metadata.EpisodeText);
        Assert.Null(metadata.AnimeId);
    }
}
