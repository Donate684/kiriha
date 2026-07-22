using System;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.Services.Data;

public partial class AnimeService
{
    public async Task AddOrUpdateAnimeAsync(AnimeItem item)
    {
        // If we're adding it back, remove from recently deleted blacklist
        lock (_recentlyDeletedIds)
        {
            if (_recentlyDeletedIds.TryGetValue(item.Id, out var cts))
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException) { }
                _recentlyDeletedIds.Remove(item.Id);
            }
        }

        // Read Collection and apply CopyTo on the UI thread: ObservableCollection is not thread-safe
        // and AnimeItem property setters raise PropertyChanged that UI bindings must observe on UI.
        var existing = await _uiDispatcher.InvokeAsync(() =>
        {
            _idIndex.TryGetValue(item.Id, out var found);
            if (found != null)
            {
                item.CopyTo(found);
            }
            else
            {
                Collection.Add(item);
                _idIndex[item.Id] = item;
            }
            return found;
        });

        await _userAnimeRepo.UpdateAsync(existing ?? item);
    }

    public async Task RemoveAnimeAsync(int animeId)
    {
        // Add to temporary blacklist to prevent re-adding during sync race conditions
        var newCts = new CancellationTokenSource();
        lock (_recentlyDeletedIds)
        {
            if (_recentlyDeletedIds.TryGetValue(animeId, out var oldCts))
            {
                try
                {
                    oldCts.Cancel();
                }
                catch (ObjectDisposedException) { }
            }
            _recentlyDeletedIds[animeId] = newCts;
        }

        _ = _backgroundTasks.Run("AnimeService.RecentDeleteExpiry", async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when canceled by another operation
            }
            finally
            {
                lock (_recentlyDeletedIds)
                {
                    if (_recentlyDeletedIds.TryGetValue(animeId, out var currentCts) && currentCts == newCts)
                    {
                        _recentlyDeletedIds.Remove(animeId);
                    }
                }
                newCts.Dispose();
            }
        });

        await _uiDispatcher.InvokeAsync(() =>
        {
            if (_idIndex.TryGetValue(animeId, out var item))
            {
                Collection.Remove(item);
                _idIndex.TryRemove(animeId, out _);
            }
        });

        // Remove locally first so the UI is responsive even when offline.
        await _userAnimeRepo.DeleteAsync(animeId);

        // Persist a Remove sync task so the deletion is replayed against trackers
        // when the network/auth is available again. Without this, an offline delete
        // would silently fail and the next remote sync would re-add the anime.
        try
        {
            await _syncManager.EnqueueRemoveAsync(animeId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AnimeService: Failed to enqueue Remove sync task for {AnimeId}", animeId);
        }
    }

    public async Task<bool> UpdateProgressAsync(AnimeItem item, int nextProgress, UserAnimeStatus? nextStatus = null)
    {
        if ((nextStatus == UserAnimeStatus.Watching || nextStatus == UserAnimeStatus.Completed) && item.StatusDetailed == "Not yet aired")
        {
            Log.Warning("Cannot set {Title} to {Status} - it has not aired yet.", item.Title, nextStatus);
            return false;
        }

        await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
        await _syncManager.EnqueueUpdateAsync(item.Id, nextProgress, nextStatus);

        item.Progress = nextProgress;
        if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
            item.Status = nextStatus.Value;

        return true;
    }

    public async Task<UserAnimeStatus?> SmartIncrementProgressAsync(AnimeItem item, int nextProgress)
    {
        UserAnimeStatus? nextStatus = null;
        if (item.Status != UserAnimeStatus.Watching && item.Status != UserAnimeStatus.Completed)
            nextStatus = UserAnimeStatus.Watching;

        bool isManga = item.MediaKind != MediaKind.Anime;

        // Manga completion
        if (isManga && item.Chapters > 0 && nextProgress >= item.Chapters && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;
        // Anime completion
        else if (!isManga && item.TotalEpisodes > 0 && nextProgress >= item.TotalEpisodes && item.Status == UserAnimeStatus.Watching)
            nextStatus = UserAnimeStatus.Completed;

        if (isManga)
        {
            item.ChaptersRead = nextProgress;
            if (nextStatus.HasValue && nextStatus != UserAnimeStatus.None)
                item.Status = nextStatus.Value;

            await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, nextStatus);
            await _syncManager.EnqueueFullUpdateAsync(item);

            _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Read");
            return nextStatus;
        }
        else
        {
            if (await UpdateProgressAsync(item, nextProgress, nextStatus))
            {
                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, nextStatus == UserAnimeStatus.Completed ? "Completed" : "Watched");
                return nextStatus;
            }
        }

        return null;
    }

    public async Task SmartDecrementProgressAsync(AnimeItem item)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        if (isManga)
        {
            if (item.ChaptersRead > 0)
            {
                int nextProgress = item.ChaptersRead - 1;
                item.ChaptersRead = nextProgress;

                await _userAnimeRepo.UpdateProgressAsync(item, nextProgress, null);
                await _syncManager.EnqueueFullUpdateAsync(item);

                _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
            }
        }
        else
        {
            if (item.Progress > 0)
            {
                int nextProgress = item.Progress - 1;
                if (await UpdateProgressAsync(item, nextProgress))
                {
                    _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, nextProgress, "Reverted");
                }
            }
        }
    }

    public async Task SetScoreAsync(AnimeItem item, int score)
    {
        item.Score = score.ToString();
        await _userAnimeRepo.UpdateScoreAsync(item, item.Score);
        await _syncManager.EnqueueUpdateAsync(item.Id, item.Progress, score: score);
        _historyService.AddEntry(item.Id, item.Title, item.RussianTitle, item.Progress, "ScoreSet", score.ToString());
    }
}
