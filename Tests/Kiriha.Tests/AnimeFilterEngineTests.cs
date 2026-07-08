using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;

namespace Kiriha.Tests;

public sealed class AnimeFilterEngineTests
{
    [Fact]
    public void ApplySearch_MatchesAcrossTitleFieldsIgnoringCase()
    {
        var items = SampleItems();

        Assert.Equal(new[] { 1 }, items.ApplySearch("frieren").Select(x => x.Id));
        Assert.Equal(new[] { 2 }, items.ApplySearch("алхимик").Select(x => x.Id));
        Assert.Equal(new[] { 3 }, items.ApplySearch("ビバップ").Select(x => x.Id));
    }

    [Fact]
    public void ApplySearch_BlankQueryReturnsOriginalItems()
    {
        var items = SampleItems();

        Assert.Equal(new[] { 1, 2, 3, 4 }, items.ApplySearch(" ").Select(x => x.Id));
    }

    [Fact]
    public void ApplyNsfw_FalseHidesRxAndHentaiGenre()
    {
        var items = SampleItems();

        var ids = items.ApplyNsfw(filterNsfw: false).Select(x => x.Id).ToArray();

        Assert.Equal(new[] { 1, 2 }, ids);
    }

    [Fact]
    public void ApplyNsfw_TrueShowsOnlyAdultContent()
    {
        var items = SampleItems();

        var ids = items.ApplyNsfw(filterNsfw: true).Select(x => x.Id).ToArray();

        Assert.Equal(new[] { 3, 4 }, ids);
    }

    [Fact]
    public void ApplySorting_PopularityTreatsZeroAsUnknownAndMovesItLast()
    {
        var sorted = SampleItems().ApplySorting(Constants.Sorting.Popularity).Select(x => x.Id).ToArray();

        Assert.Equal(new[] { 2, 1, 3, 4 }, sorted);
    }

    [Fact]
    public void ApplySorting_ScoreUsesUserScoreForPersonalList()
    {
        var sorted = SampleItems().ApplySorting(Constants.Sorting.Score, isSeasonal: false).Select(x => x.Id).ToArray();

        Assert.Equal(new[] { 2, 1, 3, 4 }, sorted);
    }

    [Fact]
    public void ApplySorting_ScoreUsesMeanScoreForSeasonal()
    {
        var sorted = SampleItems().ApplySorting(Constants.Sorting.Score, isSeasonal: true).Select(x => x.Id).ToArray();

        Assert.Equal(new[] { 1, 3, 2, 4 }, sorted);
    }

    [Fact]
    public void ApplySorting_TitleFallbackIsStableDefault()
    {
        var sorted = SampleItems().ApplySorting("unknown").Select(x => x.Title).ToArray();

        Assert.Equal(new[] { "Cowboy Bebop", "Frieren", "Fullmetal Alchemist", "Mystery Adult" }, sorted);
    }

    private static List<AnimeItem> SampleItems() =>
    [
        new AnimeItem
        {
            Id = 1,
            Title = "Frieren",
            EnglishTitle = "Frieren: Beyond Journey's End",
            RussianTitle = "Провожающая Фрирен",
            JapaneseTitle = "葬送のフリーレン",
            Score = "8.9",
            MeanScore = "9,1",
            Popularity = 10,
            AiringDate = new DateTime(2023, 9, 29),
        },
        new AnimeItem
        {
            Id = 2,
            Title = "Fullmetal Alchemist",
            RussianTitle = "Стальной алхимик",
            Score = "9.5",
            MeanScore = "8.8",
            Popularity = 1,
            AiringDate = new DateTime(2009, 4, 5),
        },
        new AnimeItem
        {
            Id = 3,
            Title = "Cowboy Bebop",
            JapaneseTitle = "カウボーイビバップ",
            Score = "-",
            MeanScore = "9.0",
            Popularity = 100,
            Rating = "rx",
            AiringDate = new DateTime(1998, 4, 3),
        },
        new AnimeItem
        {
            Id = 4,
            Title = "Mystery Adult",
            Score = "bad",
            MeanScore = "bad",
            Popularity = 0,
            Genres = new List<string> { "Hentai" },
        },
    ];
}
