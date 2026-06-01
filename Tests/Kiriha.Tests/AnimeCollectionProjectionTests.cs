using System.Collections.Specialized;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Tests;

public sealed class AnimeCollectionProjectionTests
{
    [Fact]
    public void Rebuild_ComputesStatusBucketsWithRewatchingInWatching()
    {
        using var projection = new AnimeCollectionProjection();

        projection.Rebuild(
        [
            Item(1, "Watching", UserAnimeStatus.Watching),
            Item(2, "Completed", UserAnimeStatus.Completed),
            Item(3, "Rewatching", UserAnimeStatus.Completed, isRewatching: true),
        ]);

        Assert.Equal(2, projection.Count(UserAnimeStatus.Watching));
        Assert.Equal(1, projection.Count(UserAnimeStatus.Completed));
    }

    [Fact]
    public void Query_UsesPrecomputedSearchAndNsfwFlagsInsideSelectedStatus()
    {
        using var projection = new AnimeCollectionProjection();

        projection.Rebuild(
        [
            Item(1, "Frieren", UserAnimeStatus.Watching, russianTitle: "Volshebnitsa"),
            Item(2, "Adult Frieren", UserAnimeStatus.Watching, rating: "rx"),
            Item(3, "Frieren Completed", UserAnimeStatus.Completed),
        ]);

        var sfw = projection.Query(UserAnimeStatus.Watching, "volshebnitsa", filterNsfw: false, sortBy: Constants.Sorting.Title);
        var nsfw = projection.Query(UserAnimeStatus.Watching, "frieren", filterNsfw: true, sortBy: Constants.Sorting.Title);

        Assert.Equal(new[] { 1 }, sfw.Select(x => x.Id));
        Assert.Equal(new[] { 2 }, nsfw.Select(x => x.Id));
    }

    [Fact]
    public void ItemPropertyChange_MovesItemBetweenStatusBuckets()
    {
        using var projection = new AnimeCollectionProjection();
        var item = Item(1, "Frieren", UserAnimeStatus.PlanToWatch);

        projection.Rebuild([item]);
        item.Status = UserAnimeStatus.Watching;

        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching));
        Assert.Equal(0, projection.Count(UserAnimeStatus.PlanToWatch));
    }

    [Fact]
    public void RewatchingChange_MovesCompletedItemIntoWatchingBucket()
    {
        using var projection = new AnimeCollectionProjection();
        var item = Item(1, "Frieren", UserAnimeStatus.Completed);

        projection.Rebuild([item]);
        item.IsRewatching = true;

        Assert.Equal(1, projection.Count(UserAnimeStatus.Watching));
        Assert.Equal(0, projection.Count(UserAnimeStatus.Completed));
    }

    [Fact]
    public void ApplyCollectionChange_AddsAndRemovesIncrementally()
    {
        using var projection = new AnimeCollectionProjection();
        var added = Item(1, "Frieren", UserAnimeStatus.Watching);

        projection.ApplyCollectionChange(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added),
            [added]);

        projection.ApplyCollectionChange(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, added),
            []);

        Assert.Equal(0, projection.Count(UserAnimeStatus.Watching));
    }

    private static AnimeItem Item(
        int id,
        string title,
        UserAnimeStatus status,
        string? russianTitle = null,
        string? rating = null,
        bool isRewatching = false)
    {
        return new AnimeItem
        {
            Id = id,
            Title = title,
            RussianTitle = russianTitle,
            Status = status,
            Rating = rating,
            IsRewatching = isRewatching,
        };
    }
}
