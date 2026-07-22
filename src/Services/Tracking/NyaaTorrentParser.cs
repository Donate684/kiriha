using System;
using System.Linq;
using System.Xml.Linq;
using Kiriha.Models;

namespace Kiriha.Services.Tracking;

internal static partial class NyaaTorrentParser
{
    public static TorrentItem? ParseItem(XElement item)
    {
        string? title = item.Element("title")?.Value;
        if (string.IsNullOrEmpty(title)) return null;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var animeTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
        var episodeStr = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
        var resolution = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementVideoResolution)?.Value;
        var group = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementReleaseGroup)?.Value;

        var nyaaNs = XNamespace.Get("https://nyaa.si/xmlns/nyaa");
        var infoHash = item.Element(nyaaNs + "infoHash")?.Value;

        return new TorrentItem
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
    public static bool NeedsNyaaCheck(AnimeItem anime)
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

    public static int? ExtractSingleEpisodeNumber(string title)
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

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);

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
}
