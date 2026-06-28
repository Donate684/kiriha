using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kiriha.Core;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Tracking;

public partial class RssFeedService
{
    private readonly HttpClient _httpClient;
    private readonly AnimeService _animeService;
    private readonly MappingService _mappingService;
    private readonly HttpConditionalCache _httpCache;
    private readonly IUiDispatcher _uiDispatcher;

    public System.Collections.ObjectModel.ObservableCollection<Kiriha.Models.TorrentItem> TorrentItems { get; } = new();

    private const string NyaaRssUrl = "https://nyaa.si/?page=rss&c=1_2"; // Anime - English-translated

    public RssFeedService(
        IHttpClientFactory httpClientFactory,
        AnimeService animeService,
        MappingService mappingService,
        IHttpCacheRepository httpCacheRepo,
        IUiDispatcher uiDispatcher)
    {
        _httpClient = httpClientFactory.CreateClient("RssClient");
        _animeService = animeService;
        _mappingService = mappingService;
        _uiDispatcher = uiDispatcher;
        _httpCache = new HttpConditionalCache(_httpClient, httpCacheRepo, "Nyaa");
    }

    /// <summary>
    /// Conditional GET for a Nyaa RSS URL. Nyaa returns ETag / Last-Modified
    /// for both the global feed and per-query search RSS, so a 304 round-trip
    /// is essentially free body-wise. The 30-day http_response_cache TTL means
    /// even after a long offline gap we still get correct data — the conditional
    /// GET validates against the origin every call.
    /// </summary>
    private async Task<XDocument?> FetchRssAsync(string url, CancellationToken ct)
    {
        var bytes = await _httpCache.SendAsync(
            requestFactory: innerCt =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                // RssClient already sets User-Agent globally; no extra headers needed.
                return Task.FromResult(request);
            },
            ct: ct);

        if (bytes == null || bytes.Length == 0) return null;

        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            return XDocument.Load(ms);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RssFeedService: failed to parse Nyaa RSS XML for {Url}", url);
            return null;
        }
    }

    private Kiriha.Models.TorrentItem? ParseNyaaItem(XElement item)
    {
        string? title = item.Element("title")?.Value;
        if (string.IsNullOrEmpty(title)) return null;

        var parsed = Kiriha.Utils.AnimeParseCache.Parse(title);
        var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
        var episodeStr = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
        var resolution = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementVideoResolution)?.Value;
        var group = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementReleaseGroup)?.Value;

        var nyaaNs = XNamespace.Get("https://nyaa.si/xmlns/nyaa");
        var infoHash = item.Element(nyaaNs + "infoHash")?.Value;

        return new Kiriha.Models.TorrentItem
        {
            Title = title,
            AnimeTitle = animeTitle,
            Episode = episodeStr,
            Resolution = resolution,
            ReleaseGroup = group,
            DownloadLink = item.Element("link")?.Value,
            MagnetLink = !string.IsNullOrEmpty(infoHash) ? $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}" : null,
            PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.Now,
            IsNew = false
        };
    }

    /// <summary>
    /// Anime needs a Nyaa probe only when we're actively awaiting a new episode:
    /// currently airing + watching, and NextEpisodeAt is either unknown or already overdue
    /// (the "orange" state in the UI). If next episode is known and still in the future,
    /// Nyaa torrents cannot tell us anything new.
    /// </summary>
    private static bool NeedsNyaaCheck(Kiriha.Models.AnimeItem anime)
    {
        if (anime.StatusDetailed != "currently_airing") return false;
        if (anime.Status != Kiriha.Models.Entities.UserAnimeStatus.Watching) return false;
        if (anime.NextEpisodeAt.HasValue && anime.NextEpisodeAt.Value > DateTime.Now) return false;
        return true;
    }

    /// <summary>
    /// Extracts a single trustworthy episode number from a Nyaa torrent title.
    /// Returns <c>null</c> for batch/range/multi-episode releases (e.g. "01-12",
    /// "01 ~ 12", "Batch", "Complete", "02+03") whose Anitomy parse would yield
    /// an inflated max. Single-episode releases pass through unchanged.
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"\b(batch|complete|completed|seasons?\s+\d+\s*[-~]\s*\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BatchRegex();

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\d.])(?:E|EP|Episode\s+|-\s+)?\d{1,3}\s*[-~\u2013\u2014]\s*\d{1,3}(?![\dpx])", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex RangeRegex();

    private static int? ExtractSingleEpisodeNumber(string title)
    {
        if (string.IsNullOrEmpty(title)) return null;

        // Keyword-based batch detection. Run on the raw title because Anitomy
        // sometimes folds these into ElementOther / drops them entirely.
        if (BatchRegex().IsMatch(title))
            return null;

        // Range pattern: NN-MM, NN~MM, NN–MM, NN..MM where both sides are 1-3 digit
        // episode numbers. Restricted to a leading word boundary + non-resolution
        // context so we don't reject "1080p" / "S01E05" by accident.
        if (RangeRegex().IsMatch(title))
            return null;

        var parsed = Kiriha.Utils.AnimeParseCache.Parse(title);

        // Volume token = batch (e.g. "Vol. 1" of a BD release set).
        if (parsed.Any(x => x.Category == AnitomySharp.Element.ElementCategory.ElementVolumeNumber))
            return null;

        var epEls = parsed.Where(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)
                          .Select(x => x.Value)
                          .Where(v => int.TryParse(v, out _))
                          .Select(int.Parse)
                          .Distinct()
                          .ToList();

        // Exactly one distinct episode number = trustworthy single release.
        if (epEls.Count != 1) return null;
        return epEls[0];
    }

    /// <summary>
    /// Probe Nyaa search for a real-world airing signal. Returns the highest
    /// single-episode number found across trusted single-release torrents that
    /// map back to <paramref name="anime"/>, or <c>null</c> when nothing usable
    /// is found / the anime is not currently waiting for an episode.
    ///
    /// Caller (typically <c>AiringInfoService</c>) decides whether to trust the
    /// value — e.g. capping it by schedule+1 to reject hallucinated/junk hits.
    /// This method intentionally does not mutate the AnimeItem or fire
    /// notifications, so all airing-state writes flow through one path.
    /// </summary>
    public async Task<int?> SyncEpisodesFromNyaaAsync(Kiriha.Models.AnimeItem anime, CancellationToken ct = default)
    {
        if (anime == null) return null;
        if (!NeedsNyaaCheck(anime))
        {
            Log.Debug("RssFeedService: Skipping Nyaa probe for {Title} - next episode not due yet", anime.Title);
            return null;
        }

        try
        {
            Log.Debug("RssFeedService: Syncing {Title} from Nyaa.si search...", anime.Title);
            string url = $"https://nyaa.si/?page=rss&q={Uri.EscapeDataString(anime.Title)}&c=1_2";
            var doc = await FetchRssAsync(url, ct);
            if (doc == null) return null;

            var items = doc.Descendants("item").Take(20).ToList(); // Top 20 results are enough

            int maxFound = 0;
            foreach (var item in items)
            {
                string? title = item.Element("title")?.Value;
                if (string.IsNullOrEmpty(title)) continue;

                int? epNum = ExtractSingleEpisodeNumber(title);
                if (epNum == null) continue; // batch / range / multi-ep — skip

                var parsed = Kiriha.Utils.AnimeParseCache.Parse(title);
                var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                if (string.IsNullOrEmpty(animeTitle)) continue;

                int? malId = await _mappingService.GetIdFromTitleAsync(animeTitle, new[] { anime });
                if (malId != anime.Id) continue;

                if (epNum.Value > maxFound) maxFound = epNum.Value;
            }

            return maxFound > 0 ? maxFound : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RssFeedService: Nyaa sync failed for {Title}", anime.Title);
            return null;
        }
    }

    public async Task<List<Kiriha.Models.TorrentItem>> SearchTorrentsAsync(string query)
    {
        try
        {
            string url = $"https://nyaa.si/?page=rss&q={Uri.EscapeDataString(query)}&c=1_2";
            Log.Information("Torrents: Fetching RSS from Nyaa: {Url}", url);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var doc = await FetchRssAsync(url, cts.Token);
            if (doc == null) return new List<Kiriha.Models.TorrentItem>();

            Log.Information("Torrents: Parsing XML response...");
            var items = doc.Descendants("item").ToList();
            Log.Information("Torrents: Found {Count} items in XML", items.Count);
            var results = new List<Kiriha.Models.TorrentItem>();

            // Snapshot ObservableCollection on UI thread to avoid "Collection was modified" races.
            var activeAnime = await _uiDispatcher.InvokeAsync(() =>
                _animeService.Collection
                    .Where(x => x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch)
                    .ToList());

            foreach (var item in items)
            {
                var torrent = ParseNyaaItem(item);
                if (torrent == null) continue;

                // Match only if this torrent contains an episode the user hasn't watched yet
                if (!string.IsNullOrEmpty(torrent.AnimeTitle))
                {
                    var matchedAnime = activeAnime.FirstOrDefault(x =>
                        string.Equals(x.Title, torrent.AnimeTitle, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.EnglishTitle, torrent.AnimeTitle, StringComparison.OrdinalIgnoreCase));

                    if (matchedAnime != null
                        && int.TryParse(torrent.Episode, out var epNum)
                        && epNum > matchedAnime.Progress)
                    {
                        torrent.IsMatched = true;
                    }
                }

                results.Add(torrent);
            }
            return results;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RssFeedService: Search failed for {Query}", query);
            return new List<Kiriha.Models.TorrentItem>();
        }
    }

    public async Task CheckFeedsAsync()
    {
        // Gate: only hit Nyaa if at least one watching anime is actually awaiting a new episode
        // (NextEpisodeAt is null or already passed). Otherwise torrents have nothing new for us.
        // Snapshot on UI thread — ObservableCollection is not thread-safe.
        var awaitingEpisode = await _uiDispatcher.InvokeAsync(() =>
            _animeService.Collection.Any(NeedsNyaaCheck));
        if (!awaitingEpisode)
        {
            Log.Debug("RssFeedService: Skipping Nyaa RSS check - no anime is awaiting a new episode");
            return;
        }

        Log.Debug("RssFeedService: Checking Nyaa.si RSS feed...");
        
        try 
        {
            var doc = await FetchRssAsync(NyaaRssUrl, CancellationToken.None);
            if (doc == null) return;

            var items = doc.Descendants("item").ToList();

            // Get only ongoing/watching items to save resources.
            // Snapshot on UI thread — ObservableCollection is not thread-safe.
            var activeAnime = await _uiDispatcher.InvokeAsync(() =>
                _animeService.Collection
                    .Where(x => x.Status == UserAnimeStatus.Watching || x.Status == UserAnimeStatus.PlanToWatch)
                    .ToList());

            if (!activeAnime.Any()) return;

            var newTorrents = new List<Kiriha.Models.TorrentItem>();

            foreach (var item in items)
            {
                string? title = item.Element("title")?.Value;
                if (string.IsNullOrEmpty(title)) continue;

                // Check if already in collection
                var existing = TorrentItems.FirstOrDefault(x => x.Title == title);
                if (existing != null && existing.IsMatched) continue;

                // Parse title with Anitomy
                var parsed = Kiriha.Utils.AnimeParseCache.Parse(title);
                var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                var episodeStr = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
                var resolution = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementVideoResolution)?.Value;
                var group = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementReleaseGroup)?.Value;

                // Single-episode releases only — batches / ranges return null and
                // are surfaced as torrent rows but not used to bump EpisodesAired.
                var nyaaNs = XNamespace.Get("https://nyaa.si/xmlns/nyaa");
                var infoHash = item.Element(nyaaNs + "infoHash")?.Value;

                Kiriha.Models.TorrentItem torrent;
                if (existing != null)
                {
                    torrent = existing;
                }
                else
                {
                    torrent = new Kiriha.Models.TorrentItem
                    {
                        Title = title,
                        AnimeTitle = animeTitle,
                        Episode = episodeStr,
                        Resolution = resolution,
                        ReleaseGroup = group,
                        DownloadLink = item.Element("link")?.Value,
                        MagnetLink = !string.IsNullOrEmpty(infoHash) ? $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(title)}" : null,
                        PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.Now,
                        IsNew = true
                    };
                }

                // Match with user list
                string matchTitle = !string.IsNullOrEmpty(animeTitle) ? animeTitle : title;
                int? malId = await _mappingService.GetIdFromTitleAsync(matchTitle, activeAnime);
                
                if (malId != null)
                {
                    torrent.IsMatched = true;
                }
                
                if (existing == null)
                {
                    newTorrents.Add(torrent);
                }
            }

            if (newTorrents.Any())
            {
                _uiDispatcher.Post(() => {
                    // Add to the beginning of collection
                    foreach (var t in newTorrents.OrderBy(x => x.PublishDate))
                    {
                        if (!TorrentItems.Any(existing => existing.Title == t.Title))
                        {
                            TorrentItems.Insert(0, t);
                        }
                    }
                    
                    // Trim collection
                    while (TorrentItems.Count > 100) TorrentItems.RemoveAt(TorrentItems.Count - 1);
                });
            }

            Log.Debug("RssFeedService: RSS check completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RssFeedService: Error during feed check");
        }
    }
}

