using System;

namespace Kiriha.Models;

public class ParsedMedia
{
    public string OriginalTitle { get; set; } = string.Empty;
    public string AnimeTitle { get; set; } = string.Empty;
    public string EpisodeTitle { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    /// <summary>
    /// OS process id of the player. 0 when unknown. Populated by
    /// <c>DetectionManager</c> so downstream consumers (audio-session probe,
    /// scrobbler) can identify the source process without re-scanning.
    /// </summary>
    public uint Pid { get; set; }
    public bool IsPlaying { get; set; } = true;
    
    // Additional properties expected by UI/ViewModels
    public string VideoResolution { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string AnimeType { get; set; } = string.Empty;
    public TimeSpan? Position { get; set; }
    public TimeSpan? Duration { get; set; }
}
