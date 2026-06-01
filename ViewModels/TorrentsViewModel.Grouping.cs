using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Models;

namespace Kiriha.ViewModels;

public partial class TorrentsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private TorrentSortMode _sortMode = TorrentSortMode.Newest;

    partial void OnSortModeChanged(TorrentSortMode value)
    {
        RebuildGroupedTorrents();
    }

    private void RebuildGroupedTorrents()
    {
        GroupedTorrents.Clear();

        foreach (var group in TorrentGrouping.Build(Torrents, SortMode))
        {
            GroupedTorrents.Add(group);
        }
    }
}

internal static class TorrentGrouping
{
    public static IEnumerable<TorrentGroup> Build(IEnumerable<TorrentItem> torrents, TorrentSortMode sortMode)
    {
        var source = sortMode switch
        {
            TorrentSortMode.Matched =>
                torrents.OrderByDescending(t => t.IsMatched)
                    .ThenByDescending(t => t.PublishDate),
            TorrentSortMode.ReleaseGroup =>
                torrents.OrderBy(t => string.IsNullOrEmpty(t.ReleaseGroup) ? "zzz" : t.ReleaseGroup,
                        System.StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(t => t.PublishDate),
            _ => torrents.OrderByDescending(t => t.PublishDate),
        };

        return source
            .GroupBy(t => string.IsNullOrWhiteSpace(t.AnimeTitle) ? "\u2014" : t.AnimeTitle!.Trim(),
                System.StringComparer.OrdinalIgnoreCase)
            .Select(g => new TorrentGroup(g.Key, g.ToList()))
            .OrderByDescending(g => g.HasMatch)
            .ThenByDescending(g => g.LatestDate);
    }
}
