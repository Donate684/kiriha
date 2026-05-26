using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Tests;

public sealed class AnimeItemTests
{
    [Fact]
    public void DisplayTitle_AndSynopsisPreferRussianWhenPresent()
    {
        var item = new AnimeItem
        {
            Title = "Frieren: Beyond Journey's End",
            RussianTitle = "Провожающая в последний путь Фрирен",
            Synopsis = "English synopsis",
            RussianSynopsis = "Russian synopsis"
        };

        Assert.Equal("Провожающая в последний путь Фрирен", item.DisplayTitle);
        Assert.Equal("Russian synopsis", item.DisplaySynopsis);
    }

    [Theory]
    [InlineData(6, 12, 50)]
    [InlineData(20, 24, 83.33333333333334)]
    [InlineData(30, 0, 83.33333333333334)]
    [InlineData(0, 0, 0)]
    public void ProgressValue_UsesKnownTotalOrBucketedFallback(int progress, int total, double expected)
    {
        var item = new AnimeItem { Progress = progress, TotalEpisodes = total };

        Assert.Equal(expected, item.ProgressValue, precision: 6);
    }

    [Fact]
    public void AiredProgress_ShowsOnlyWhenWatchingHasUnseenAiredEpisodes()
    {
        var item = new AnimeItem
        {
            Status = UserAnimeStatus.Watching,
            Progress = 3,
            TotalEpisodes = 12,
            EpisodesAired = 5,
            StatusDetailed = "currently_airing"
        };

        Assert.True(item.ShowAiredProgressBar);
        Assert.Equal(2, item.UnseenEpisodesCount);
        Assert.Equal(5d / 12d, item.AiredValueFraction, precision: 6);
    }

    [Fact]
    public void Clone_CopiesCollectionsWithoutSharingListInstances()
    {
        var item = new AnimeItem
        {
            Id = 1,
            Title = "Test",
            Genres = new List<string> { "Action" },
            Studios = new List<string> { "Bones" },
            AlternativeTitles = new List<string> { "Alt" }
        };

        var clone = item.Clone();
        clone.Genres.Add("Drama");
        clone.Studios.Add("Trigger");
        clone.AlternativeTitles.Add("Other");

        Assert.Equal(new[] { "Action" }, item.Genres);
        Assert.Equal(new[] { "Bones" }, item.Studios);
        Assert.Equal(new[] { "Alt" }, item.AlternativeTitles);
    }
}
