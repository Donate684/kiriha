using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Api;

public partial class ShikiApiService
{
    public Task<List<AnimeItem>?> GetUserAnimeListAsync(CancellationToken ct = default)
    {
        Log.Warning("Shikimori full-list sync is disabled; skipping destructive local mirror update.");
        return Task.FromResult<List<AnimeItem>?>(null);
    }

    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        // No tokens at all — user disabled the tracker. Nothing to retry.
        if (_settingsService.Current.Api.Shiki == null) return SyncOutcome.PermanentFailure;

        if (_settingsService.Current.Api.Shiki.UserId == null)
        {
            var userId = await GetCurrentUserIdAsync(ct);
            if (userId == null) return SyncOutcome.TransientFailure;
            var current = _settingsService.Current.Api.Shiki;
            if (current == null) return SyncOutcome.PermanentFailure;
            _settingsService.Update(settings =>
            {
                if (settings.Api.Shiki != null) settings.Api.Shiki.UserId = userId;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
        }

        // Re-read to pick up any token-refresh side effects.
        var tokens = _settingsService.Current.Api.Shiki;
        if (tokens == null) return SyncOutcome.PermanentFailure;

        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = tokens.UserId!,
            ["target_id"] = animeId,
            ["target_type"] = "Anime",
            ["episodes"] = episodes
        };

        var shikiStatus = StatusMapper.ToShiki(status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;
        if (score.HasValue && score.Value > 0) userRate["score"] = score.Value;
        if (isRewatching.HasValue) userRate["is_rewatching"] = isRewatching.Value;
        if (rewatchCount.HasValue) userRate["rewatches"] = rewatchCount.Value;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeItem item, CancellationToken ct = default)
    {
        var tokens = _settingsService.Current.Api.Shiki;
        if (tokens == null) return SyncOutcome.PermanentFailure;

        if (tokens.UserId == null)
        {
            var userId = await GetCurrentUserIdAsync(ct);
            if (userId == null) return SyncOutcome.TransientFailure;
            _settingsService.Update(settings =>
            {
                if (settings.Api.Shiki != null) settings.Api.Shiki.UserId = userId;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
            tokens = _settingsService.Current.Api.Shiki;
            if (tokens == null) return SyncOutcome.PermanentFailure;
        }

        bool isManga = item.MediaKind != MediaKind.Anime;
        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = tokens.UserId!,
            ["target_id"] = item.Id,
            ["target_type"] = isManga ? "Manga" : "Anime"
        };

        if (isManga)
        {
            userRate["chapters"] = item.ChaptersRead;
            userRate["volumes"] = item.VolumesRead;
        }
        else
        {
            userRate["episodes"] = item.Progress;
        }

        var shikiStatus = StatusMapper.ToShiki(item.Status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;
        if (int.TryParse(item.Score, out var score) && score > 0) userRate["score"] = score;
        userRate["is_rewatching"] = item.IsRewatching;
        userRate["rewatches"] = item.RewatchCount;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public Task<List<AnimeItem>?> GetUserMangaListAsync(CancellationToken ct = default)
    {
        Log.Warning("Shikimori full-list manga sync is disabled; skipping destructive local mirror update.");
        return Task.FromResult<List<AnimeItem>?>(null);
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default)
    {
        if (_settingsService.Current.Api.Shiki == null) return SyncOutcome.PermanentFailure;

        if (_settingsService.Current.Api.Shiki.UserId == null)
        {
            var userId = await GetCurrentUserIdAsync(ct);
            if (userId == null) return SyncOutcome.TransientFailure;
            var current = _settingsService.Current.Api.Shiki;
            if (current == null) return SyncOutcome.PermanentFailure;
            _settingsService.Update(settings =>
            {
                if (settings.Api.Shiki != null) settings.Api.Shiki.UserId = userId;
            }, SettingsSection.Api, save: false);
            _settingsService.SaveImmediate();
        }

        var tokens = _settingsService.Current.Api.Shiki;
        if (tokens == null) return SyncOutcome.PermanentFailure;

        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = tokens.UserId!,
            ["target_id"] = mangaId,
            ["target_type"] = "Manga",
            ["chapters"] = chapters
        };

        if (volumes.HasValue) userRate["volumes"] = volumes.Value;

        var shikiStatus = StatusMapper.ToShiki(status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;

        if (score.HasValue && score.Value > 0) userRate["score"] = score.Value;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default)
    {
        // Shikimori deletes by user_rate_id, not anime_id. Until the service tracks
        // user_rate_id locally, treat remove as a no-op so SyncManager doesn't
        // endlessly retry and clutter history with SyncFailed entries.
        Log.Warning("ShikiApiService: remove is a no-op until user_rate_id is tracked locally ({AnimeId}).", animeId);
        return Task.FromResult(SyncOutcome.Success);
    }
}
