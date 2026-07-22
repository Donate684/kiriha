using System;
using System.Linq;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Utils.Parsing;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Kiriha.Services;

public interface IPlayerMediaMetadataResolver
{
    PlayerMediaMetadata Resolve(string videoPath);
}

public sealed class PlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PlayerMediaMetadataResolver(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public PlayerMediaMetadata Resolve(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return PlayerMediaMetadata.FromVideoPath(videoPath);

        try
        {
            var filename = System.IO.Path.GetFileNameWithoutExtension(videoPath);
            var filenameToParse = System.Text.RegularExpressions.Regex.Replace(filename, @"([sS]?\d*[eE]\d+)\s*-(.*)", "$1 - $2");
            var parsed = AnimeParseCache.Parse(filenameToParse);

            var extractedTitle = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
            var episodeText = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value ?? string.Empty;
            var seasonElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeSeason)?.Value;

            string originalTitle = filename;
            bool isEmber = videoPath.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(videoPath);
            if (isEmber)
            {
                string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(videoPath);
                if (!string.IsNullOrEmpty(meaningfulDir))
                {
                    var dirParsed = AnimeParseCache.Parse(meaningfulDir);
                    var dirTitle = dirParsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                    extractedTitle = dirTitle ?? meaningfulDir;
                    originalTitle = meaningfulDir;
                }
            }

            if (string.IsNullOrWhiteSpace(extractedTitle))
                return PlayerMediaMetadata.FromVideoPath(videoPath) with { EpisodeText = episodeText };

            int.TryParse(seasonElement, out int parsedSeason);
            var searchTitle = parsedSeason > 1
                ? $"{extractedTitle} Season {parsedSeason}"
                : extractedTitle;

            using var db = _dbFactory.CreateDbContext();
            var extractedLower = extractedTitle.ToLower();
            var searchLower = searchTitle.ToLower();

            var match = db.UserAnime.FirstOrDefault(a =>
                (a.RussianTitle != null && a.RussianTitle.ToLower() == searchLower) ||
                (a.EnglishTitle != null && a.EnglishTitle.ToLower() == searchLower) ||
                (a.Title != null && a.Title.ToLower() == searchLower));

            if (match == null && parsedSeason <= 1)
            {
                match = db.UserAnime.FirstOrDefault(a =>
                    (a.RussianTitle != null && a.RussianTitle.ToLower() == extractedLower) ||
                    (a.EnglishTitle != null && a.EnglishTitle.ToLower() == extractedLower) ||
                    (a.Title != null && a.Title.ToLower() == extractedLower));
            }

            return match == null
                ? new PlayerMediaMetadata(originalTitle, extractedTitle, string.Empty, episodeText, null)
                : new PlayerMediaMetadata(
                    originalTitle,
                    match.RussianTitle ?? match.Title ?? extractedTitle,
                    match.EnglishTitle ?? match.Title ?? string.Empty,
                    episodeText,
                    match.Id);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve player media metadata for {Path}", videoPath);
            return PlayerMediaMetadata.FromVideoPath(videoPath);
        }
    }
}
