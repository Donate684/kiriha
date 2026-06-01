using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Serilog;

namespace Kiriha.ViewModels;

public partial class TorrentsViewModel
{
    partial void OnSelectedAnimeChanged(AnimeItem? value)
    {
        if (FiltersPerTitle)
        {
            ReloadFiltersForCurrentContext();
        }

        if (value != null)
        {
            SearchQuery = value.Title;
            PerformSearchCommand.Execute(null);
        }
    }

    [RelayCommand]
    public async Task PerformSearch()
    {
        string query = TorrentQueryBuilder.Build(SearchQuery, new TorrentQueryFilters(
            FilterVaryg,
            FilterEraiRaws,
            FilterToonsHub,
            Filter1080p,
            FilterHevc,
            OnlyCrunchyroll,
            FilterNetflix,
            FilterAmazon,
            FilterHidive));

        if (string.IsNullOrEmpty(query))
        {
            Log.Debug("Torrents: Query is empty, clearing results");
            Torrents.Clear();
            return;
        }

        try
        {
            IsLoading = true;
            Log.Information("Torrents: Starting search for: {Query}", query);

            var results = await _rssService.SearchTorrentsAsync(query);
            Log.Information("Torrents: Search returned {Count} items", results.Count);

            Torrents.Clear();
            foreach (var r in results) Torrents.Add(r);
            RebuildGroupedTorrents();

            if (!results.Any())
            {
                Log.Warning("Torrents: No results found for: {Query}", query);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Torrents: Search failed for: {Query}", query);
        }
        finally
        {
            IsLoading = false;
            Log.Debug("Torrents: Search process completed (loading set to false)");
        }
    }

    [RelayCommand]
    public void SelectAnime(AnimeItem? anime)
    {
        SelectedAnime = anime;
    }

    [RelayCommand]
    public void DownloadMagnet(TorrentItem torrent)
    {
        if (torrent == null || string.IsNullOrEmpty(torrent.MagnetLink)) return;
        UIUtils.OpenUrl(torrent.MagnetLink);
    }

    [RelayCommand]
    public void DownloadTorrentFile(TorrentItem torrent)
    {
        if (torrent == null || string.IsNullOrEmpty(torrent.DownloadLink)) return;
        UIUtils.OpenUrl(torrent.DownloadLink);
    }

    [RelayCommand]
    public void Refresh()
    {
        // RssFeedService checks automatically, but we could trigger a manual check here if needed.
    }

    [RelayCommand]
    public void ClearSelectedAnime()
    {
        SelectedAnime = null;
        SearchQuery = string.Empty;
        Torrents.Clear();
        RebuildGroupedTorrents();
    }
}

internal readonly record struct TorrentQueryFilters(
    bool FilterVaryg,
    bool FilterEraiRaws,
    bool FilterToonsHub,
    bool Filter1080p,
    bool FilterHevc,
    bool OnlyCrunchyroll,
    bool FilterNetflix,
    bool FilterAmazon,
    bool FilterHidive);

internal static class TorrentQueryBuilder
{
    public static string Build(string? baseQuery, TorrentQueryFilters filters)
    {
        var query = baseQuery?.Trim() ?? string.Empty;

        query = Append(query, filters.FilterVaryg, "VARYG");
        query = Append(query, filters.FilterEraiRaws, "Erai-raws");
        query = Append(query, filters.FilterToonsHub, "ToonsHub");
        query = Append(query, filters.Filter1080p, "1080p");
        query = Append(query, filters.FilterHevc, "HEVC");
        query = Append(query, filters.OnlyCrunchyroll, "CR");
        query = Append(query, filters.FilterNetflix, "NF");
        query = Append(query, filters.FilterAmazon, "AMZN");
        query = Append(query, filters.FilterHidive, "HIDIVE");

        return query;
    }

    private static string Append(string query, bool enabled, string token)
    {
        if (!enabled) return query;
        return string.IsNullOrEmpty(query) ? token : $"{query} {token}";
    }
}
