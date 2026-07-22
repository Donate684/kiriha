namespace Kiriha.Models.Entities;

public class SyncTaskEntity
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public string Type { get; set; } = string.Empty; // "UpdateProgress", "FullUpdate"
    public int? Progress { get; set; }
    public string? Status { get; set; } // DbString format
    public int? Score { get; set; }
    public string? Payload { get; set; } // JSON of FullItem
    public int RetryCount { get; set; }
    public string? SuccessfulTrackersJson { get; set; } // JSON array of successful tracker names
}
