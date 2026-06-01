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
    public void Presentation_MatchesAnimeItemCompatibilityProperties()
    {
        var item = new AnimeItem
        {
            Title = "Original",
            RussianTitle = "Localized",
            Synopsis = "Synopsis",
            RussianSynopsis = "Localized synopsis",
            Status = UserAnimeStatus.Watching,
            Progress = 3,
            TotalEpisodes = 12,
            EpisodesAired = 5,
            StatusDetailed = "currently_airing",
            Genres = new List<string> { "Action", "Drama", "Comedy" },
            Studios = new List<string> { "Madhouse" }
        };

        var presentation = item.Presentation;

        Assert.Equal(item.DisplayTitle, presentation.DisplayTitle);
        Assert.Equal(item.DisplaySynopsis, presentation.DisplaySynopsis);
        Assert.Equal(item.ProgressValue, presentation.ProgressValue);
        Assert.Equal(item.AiredValueFraction, presentation.AiredValueFraction);
        Assert.Equal(item.ShowAiredProgressBar, presentation.ShowAiredProgressBar);
        Assert.Equal(item.UnseenEpisodesCount, presentation.UnseenEpisodesCount);
        Assert.Equal(item.TopGenres, presentation.TopGenres);
        Assert.Equal(item.HasStudios, presentation.HasStudios);
    }

    [Fact]
    public void Presentation_UsesSnapshotTimeForTimeDependentBadges()
    {
        var now = new DateTime(2026, 06, 01, 12, 00, 00);
        var item = new AnimeItem
        {
            NextEpisodeAt = now.AddHours(-49)
        };

        var presentation = new AnimeItemPresentation(item, now);

        Assert.Equal(string.Empty, presentation.AiringBadgeText);
        Assert.Equal("#FF8C00", presentation.AiringBadgeColor);
    }

    [Fact]
    public void Presentation_NotifiesWithoutLegacyComputedPropertyNoise()
    {
        var item = new AnimeItem();
        var changed = new List<string?>();
        item.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        item.Progress = 3;

        Assert.Contains(nameof(AnimeItem.Presentation), changed);
        Assert.DoesNotContain(nameof(AnimeItem.ProgressValue), changed);
        Assert.DoesNotContain(nameof(AnimeItem.ProgressValueFraction), changed);
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
