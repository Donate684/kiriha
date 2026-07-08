using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;

namespace Kiriha.ViewModels.Seasonal;

internal sealed record SeasonalFilterRequest(
    IReadOnlyList<AnimeItem> Items,
    IReadOnlyDictionary<int, UserAnimeStatus> UserStore,
    string? SearchQuery,
    string SortBy,
    bool FilterNsfw,
    string SelectedCategory,
    int CurrentYear,
    string CurrentSeason,
    IReadOnlySet<int> HiddenIds,
    bool ShowHidden,
    bool FilterNotInList,
    bool FilterWatching,
    bool FilterCompleted,
    bool FilterOnHold,
    bool FilterPlanToWatch,
    bool FilterDropped);

internal sealed record SeasonalFilterResult(
    IReadOnlyList<AnimeItem> Items,
    string Header,
    IReadOnlyDictionary<string, string> Headers,
    string ResolvedCategory);

internal static class SeasonalFilterEngine
{
    public static SeasonalFilterResult Apply(SeasonalFilterRequest request)
    {
        var filtered = ApplyBaseFilters(request)
            .ApplySearch(request.SearchQuery)
            .ApplyNsfw(request.FilterNsfw)
            .ApplySorting(request.SortBy, isSeasonal: true)
            .ToList();

        var categories = SeasonalCategoryBuckets.Build(filtered, request.CurrentYear, request.CurrentSeason);
        var resolvedCategory = categories.ResolveCategory(request.SelectedCategory);
        var items = categories.GetItems(resolvedCategory).DistinctBy(x => x.Id).ToList();
        var headers = categories.BuildHeaders();

        return new SeasonalFilterResult(
            items,
            headers[resolvedCategory],
            headers,
            resolvedCategory);
    }

    private static IEnumerable<AnimeItem> ApplyBaseFilters(SeasonalFilterRequest request)
    {
        bool anyStatusFilter = request.FilterWatching || request.FilterCompleted || request.FilterOnHold ||
                               request.FilterPlanToWatch || request.FilterDropped;
        bool anyFilter = request.FilterNotInList || anyStatusFilter || request.ShowHidden;

        if (!anyFilter)
        {
            return request.HiddenIds.Count == 0
                ? request.Items
                : request.Items.Where(x => !request.HiddenIds.Contains(x.Id));
        }

        return request.Items.Where(x =>
        {
            bool isHidden = request.HiddenIds.Count > 0 && request.HiddenIds.Contains(x.Id);
            if (isHidden) return request.ShowHidden;

            var status = request.UserStore.TryGetValue(x.Id, out var storedStatus)
                ? storedStatus
                : UserAnimeStatus.None;

            return (request.FilterNotInList && status == UserAnimeStatus.None) ||
                   (request.FilterWatching && status == UserAnimeStatus.Watching) ||
                   (request.FilterCompleted && status == UserAnimeStatus.Completed) ||
                   (request.FilterOnHold && status == UserAnimeStatus.OnHold) ||
                   (request.FilterPlanToWatch && status == UserAnimeStatus.PlanToWatch) ||
                   (request.FilterDropped && status == UserAnimeStatus.Dropped);
        });
    }
}

internal sealed class SeasonalCategoryBuckets
{
    private readonly Dictionary<string, List<AnimeItem>> _items;

    private SeasonalCategoryBuckets(Dictionary<string, List<AnimeItem>> items)
    {
        _items = items;
    }

    public static SeasonalCategoryBuckets Build(IEnumerable<AnimeItem> source, int currentYear, string currentSeason)
    {
        var buckets = new Dictionary<string, List<AnimeItem>>
        {
            ["New"] = new(),
            ["Continuing"] = new(),
            ["Movies"] = new(),
            ["OVA"] = new(),
            ["ONA"] = new(),
            ["Specials"] = new(),
            ["Other"] = new()
        };

        foreach (var item in source)
        {
            string type = (item.Type ?? "").ToLowerInvariant();
            if (type == Constants.AnimeTypes.Tv || type == Constants.AnimeTypes.TvSpecial)
            {
                if (item.StartYear == currentYear &&
                    string.Equals(item.StartSeason, currentSeason, StringComparison.OrdinalIgnoreCase))
                    buckets["New"].Add(item);
                else
                    buckets["Continuing"].Add(item);
            }
            else if (type.Contains(Constants.AnimeTypes.Movie)) buckets["Movies"].Add(item);
            else if (type == Constants.AnimeTypes.Ova) buckets["OVA"].Add(item);
            else if (type == Constants.AnimeTypes.Ona) buckets["ONA"].Add(item);
            else if (type == Constants.AnimeTypes.Special) buckets["Specials"].Add(item);
            else buckets["Other"].Add(item);
        }

        return new SeasonalCategoryBuckets(buckets);
    }

    public string ResolveCategory(string selectedCategory)
    {
        var categoryCounts = new (string Key, int Count)[]
        {
            ("New", _items["New"].Count),
            ("Continuing", _items["Continuing"].Count),
            ("Movies", _items["Movies"].Count),
            ("ONA", _items["ONA"].Count),
            ("OVA", _items["OVA"].Count),
            ("Specials", _items["Specials"].Count),
            ("Other", _items["Other"].Count)
        };

        int selectedIdx = Array.FindIndex(categoryCounts, c => c.Key == selectedCategory);
        if (selectedIdx < 0) selectedIdx = 0;
        if (categoryCounts[selectedIdx].Count > 0) return categoryCounts[selectedIdx].Key;

        for (int dist = 1; dist < categoryCounts.Length; dist++)
        {
            int right = selectedIdx + dist;
            int left = selectedIdx - dist;
            if (right < categoryCounts.Length && categoryCounts[right].Count > 0)
                return categoryCounts[right].Key;
            if (left >= 0 && categoryCounts[left].Count > 0)
                return categoryCounts[left].Key;
        }

        return categoryCounts[selectedIdx].Key;
    }

    public IReadOnlyList<AnimeItem> GetItems(string category) => _items.TryGetValue(category, out var items)
        ? items
        : _items["Other"];

    public IReadOnlyDictionary<string, string> BuildHeaders() => new Dictionary<string, string>
    {
        ["New"] = GetHeader("anime.seasonal.categories.new", _items["New"].Count),
        ["Continuing"] = GetHeader("anime.seasonal.categories.continuing", _items["Continuing"].Count),
        ["Movies"] = GetHeader("anime.seasonal.categories.movies", _items["Movies"].Count),
        ["OVA"] = GetHeader("ova", _items["OVA"].Count),
        ["ONA"] = GetHeader("ona", _items["ONA"].Count),
        ["Specials"] = GetHeader("anime.seasonal.categories.specials", _items["Specials"].Count),
        ["Other"] = GetHeader("anime.seasonal.categories.other", _items["Other"].Count)
    };

    private static string GetHeader(string key, int count)
    {
        string loc = UIUtils.GetLoc(key);
        if (loc == key && key != "TV" && key != "OVA" && key != "ONA")
            loc = char.ToUpper(loc[0]) + loc.Substring(1);
        return UIUtils.GetLoc("filters.header_format", loc, count.ToString());
    }
}
