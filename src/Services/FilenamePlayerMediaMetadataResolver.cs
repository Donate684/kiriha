using System;
using System.Linq;
using Kiriha.Models;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Serilog;

namespace Kiriha.Services;

public sealed class FilenamePlayerMediaMetadataResolver : IPlayerMediaMetadataResolver
{
    public PlayerMediaMetadata Resolve(string videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
            return PlayerMediaMetadata.FromVideoPath(videoPath);

        try
        {
            var filename = System.IO.Path.GetFileNameWithoutExtension(videoPath);
            var filenameToParse = System.Text.RegularExpressions.Regex.Replace(filename, @"([sS]?\d*[eE]\d+)\s*-(.*)", "$1 - $2");
            var parsed = AnimeParseCache.Parse(filenameToParse);

            string? title = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
            var episode = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

            string originalTitle = filename;
            bool isEmber = videoPath.Contains("EMBER", StringComparison.OrdinalIgnoreCase) || EmberTitleResolver.ScanFileForEmber(videoPath);
            if (isEmber)
            {
                string meaningfulDir = EmberTitleResolver.GetMeaningfulDirectoryName(videoPath);
                if (!string.IsNullOrEmpty(meaningfulDir))
                {
                    var dirParsed = AnimeParseCache.Parse(meaningfulDir);
                    var dirTitle = dirParsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                    title = dirTitle ?? meaningfulDir;
                    originalTitle = meaningfulDir;
                }
            }

            return new PlayerMediaMetadata(
                originalTitle,
                string.IsNullOrWhiteSpace(title) ? filename : title,
                string.Empty,
                episode ?? string.Empty,
                null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse player media metadata from {Path}", videoPath);
            return PlayerMediaMetadata.FromVideoPath(videoPath);
        }
    }
}
