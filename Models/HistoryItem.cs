using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models;

public class HistoryItem
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public string AnimeTitle { get; set; } = string.Empty;
    public string? RussianTitle { get; set; }
    public int Episode { get; set; }
    public DateTime Timestamp { get; set; }
    
    // Optional: for differentiation
    public int ActionType { get; set; } = 1; // 1: Watched, etc.
    public string Detail { get; set; } = string.Empty;

    /// <summary>Runtime-resolved poster URL (from user's AnimeService.Collection). Not persisted.</summary>
    [NotMapped]
    public string? PosterUrl { get; set; }
}
