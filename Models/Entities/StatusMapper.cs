using System;

namespace Kiriha.Models.Entities;

/// <summary>
/// Centralized status mapping between Enum, legacy DB string, MAL API, and Shikimori API.
/// </summary>
public static class StatusMapper
{
    // DB Compatibility (Maps enum to the legacy string values previously used)
    public static string ToDbString(UserAnimeStatus status)
    {
        return status switch
        {
            UserAnimeStatus.Watching => "Watching",
            UserAnimeStatus.Completed => "Completed",
            UserAnimeStatus.OnHold => "On Hold",
            UserAnimeStatus.Dropped => "Dropped",
            UserAnimeStatus.PlanToWatch => "Plan to Watch",
            UserAnimeStatus.None => "None",
            _ => "None"
        };
    }

    public static UserAnimeStatus FromDbString(string? dbStatus)
    {
        if (string.IsNullOrEmpty(dbStatus)) return UserAnimeStatus.None;
        
        // Also handling possible old mal-format directly leaked into DB just in case
        return dbStatus.ToLowerInvariant() switch
        {
            "watching" => UserAnimeStatus.Watching,
            "completed" => UserAnimeStatus.Completed,
            "on hold" => UserAnimeStatus.OnHold,
            "on_hold" => UserAnimeStatus.OnHold,
            "dropped" => UserAnimeStatus.Dropped,
            "plan to watch" => UserAnimeStatus.PlanToWatch,
            "plan_to_watch" => UserAnimeStatus.PlanToWatch,
            "planned" => UserAnimeStatus.PlanToWatch,
            _ => UserAnimeStatus.None
        };
    }

    // MAL API string (e.g. "watching") -> Enum
    public static UserAnimeStatus FromMal(string? malStatus)
    {
        if (string.IsNullOrEmpty(malStatus)) return UserAnimeStatus.None;
        return malStatus switch
        {
            "watching" or "reading" => UserAnimeStatus.Watching,
            "completed" => UserAnimeStatus.Completed,
            "on_hold" => UserAnimeStatus.OnHold,
            "dropped" => UserAnimeStatus.Dropped,
            "plan_to_watch" or "plan_to_read" => UserAnimeStatus.PlanToWatch,
            _ => UserAnimeStatus.None
        };
    }

    // Enum -> MAL API string (e.g. "watching" or "reading")
    public static string ToMal(UserAnimeStatus status, bool isManga = false)
    {
        return status switch
        {
            UserAnimeStatus.Watching => isManga ? "reading" : "watching",
            UserAnimeStatus.Completed => "completed",
            UserAnimeStatus.OnHold => "on_hold",
            UserAnimeStatus.Dropped => "dropped",
            UserAnimeStatus.PlanToWatch => isManga ? "plan_to_read" : "plan_to_watch",
            _ => ""
        };
    }

    // Enum -> Shikimori API string
    public static string? ToShiki(UserAnimeStatus? status)
    {
        if (status == null || status == UserAnimeStatus.None) return null;
        return status switch
        {
            UserAnimeStatus.Watching => "watching",
            UserAnimeStatus.Completed => "completed",
            UserAnimeStatus.OnHold => "on_hold",
            UserAnimeStatus.Dropped => "dropped",
            UserAnimeStatus.PlanToWatch => "planned", // Shiki uses 'planned'
            _ => ""
        };
    }
}
