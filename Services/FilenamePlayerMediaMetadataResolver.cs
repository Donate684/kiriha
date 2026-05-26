using System;
using System.Linq;
using Kiriha.Models;
using Kiriha.Utils;
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
            var parsed = AnimeParseCache.Parse(filename);

            var title = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
            var episode = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;

            return new PlayerMediaMetadata(
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
