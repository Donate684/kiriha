using System.Collections.Generic;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Api;

public enum SyncTaskType
{
    UpdateProgress,
    FullUpdate,
    Remove
}

public class SyncTask
{
    public int Id { get; set; }
    public int AnimeId { get; set; }
    public SyncTaskType Type { get; set; }
    public int? Progress { get; set; }
    public UserAnimeStatus? Status { get; set; }
    public int? Score { get; set; }
    public AnimeItem? FullItem { get; set; }
    public int RetryCount { get; set; } = 0;
    public HashSet<string> SuccessfulTrackers { get; set; } = new();
}
