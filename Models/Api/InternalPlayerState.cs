using System;

namespace Kiriha.Models.Api;

public class InternalPlayerState
{
    public int? AnimeId { get; set; }
    public string OriginalTitle { get; set; } = string.Empty;
    public string AnimeTitle { get; set; } = string.Empty;
    public string Episode { get; set; } = string.Empty;
    public double Position { get; set; }
    public double Duration { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsClosed { get; set; }
}
