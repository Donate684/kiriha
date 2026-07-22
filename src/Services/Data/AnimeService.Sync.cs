using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data;

public partial class AnimeService
{
    public async Task<bool> SyncWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
        if (primaryTracker == null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            status?.Report(UIUtils.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserAnimeListAsync(ct);
            if (apiList == null) return false;

            // Sanity guard: if the tracker handed back a suspiciously truncated list (e.g. silent
            // mid-pagination failure that didn't surface as null), refuse to run the destructive
            // diff. Otherwise we'd "remove" the missing half of the user's library from the local
            // DB on every retry. Threshold: lose >30% relative to current cache when cache is
            // non-trivial. The user can always do a full re-sync after restart if this triggers.
            var currentItems = await _uiDispatcher.InvokeAsync(() => Collection.Where(x => x.MediaKind == MediaKind.Anime).ToList());
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * SyncSafetyConstants.MaxAllowedTotalDropRatio)
            {
                Log.Warning("SyncWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);

            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            // Snapshot Collection on the UI thread to avoid "Collection was modified" if
            // the UI is iterating concurrently. ObservableCollection is not thread-safe.
            var snapshot = await _uiDispatcher.InvokeAsync(() => Collection.Where(x => x.MediaKind == MediaKind.Anime).ToList());
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, new[] { MediaKind.Anime });

            // Re-build recognition index after sync
            await Task.Run(() => _recognitionCache.BuildIndex(Collection));

            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to sync with {Tracker}", primaryTracker.Name);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    public async Task<bool> SyncMangaWithTrackersAsync(IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return false;

        var primaryTracker = _trackers.FirstOrDefault(t => t.IsEnabled);
        if (primaryTracker == null)
        {
            Log.Warning("No active trackers found for synchronization.");
            Interlocked.Exchange(ref _syncing, 0);
            return false;
        }

        try
        {
            status?.Report(UIUtils.GetLoc("sync.syncing.with", primaryTracker.Name));
            var apiList = await primaryTracker.GetUserMangaListAsync(ct);
            if (apiList == null) return false;

            var currentItems = await _uiDispatcher.InvokeAsync(() => Collection.Where(x => x.MediaKind == MediaKind.Manga || x.MediaKind == MediaKind.LightNovel).ToList());
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * SyncSafetyConstants.MaxAllowedTotalDropRatio)
            {
                Log.Warning("SyncMangaWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);

            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            var snapshot = await _uiDispatcher.InvokeAsync(() => Collection.Where(x => x.MediaKind == MediaKind.Manga || x.MediaKind == MediaKind.LightNovel).ToList());
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, new[] { MediaKind.Manga, MediaKind.LightNovel });

            // Reusing AnimeListRefreshMessage for now to trigger UI refresh
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error(ex, "Failed to sync manga with {Tracker}", primaryTracker.Name);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
        }
    }

    private static bool IsRemoteSnapshotSafe(List<AnimeItem> currentItems, List<AnimeItem> apiList)
    {
        static int CountStatus(List<AnimeItem> items, UserAnimeStatus status)
        {
            return status == UserAnimeStatus.Watching
                ? items.Count(x => x.Status == UserAnimeStatus.Watching || x.IsRewatching)
                : items.Count(x => x.Status == status);
        }

        foreach (var trackedStatus in new[]
        {
            UserAnimeStatus.Watching,
            UserAnimeStatus.Completed,
            UserAnimeStatus.OnHold,
            UserAnimeStatus.Dropped,
            UserAnimeStatus.PlanToWatch
        })
        {
            var local = CountStatus(currentItems, trackedStatus);
            if (local < SyncSafetyConstants.MinimumStatusGuardCount) continue;

            var incoming = CountStatus(apiList, trackedStatus);
            var dropped = local - incoming;
            if (dropped < SyncSafetyConstants.MinimumStatusDropCount) continue;

            var incomingRatio = (double)incoming / local;
            if (incomingRatio < SyncSafetyConstants.MaximumAllowedStatusDropRatio)
            {
                Log.Warning(
                    "SyncWithTrackers: aborting because {Status} count dropped suspiciously from {Local} to {Incoming}. Likely a partial or stale tracker snapshot.",
                    trackedStatus,
                    local,
                    incoming);
                return false;
            }
        }

        return true;
    }

    private async Task ProcessSyncResults(List<AnimeItem> apiList, List<AnimeItem> currentItems, IProgress<string>? status, CancellationToken ct)
    {
        var apiMap = apiList.ToDictionary(x => x.Id);

        // Snapshot Collection on UI thread — ObservableCollection is not thread-safe and a
        // background ToList() can race with concurrent UI iteration / Add.

        var existingMap = currentItems.ToDictionary(x => x.Id);

        // 1. Remove items no longer in API
        var toRemove = currentItems.Where(x => !apiMap.ContainsKey(x.Id)).ToList();
        if (toRemove.Any())
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var item in toRemove)
                {
                    Collection.Remove(item);
                    _idIndex.TryRemove(item.Id, out _);
                }
            });
        }

        // 2. Update and Add
        var uiBatch = new List<Action>();
        int total = apiList.Count;

        for (int i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested) break;

            var newItem = apiList[i];

            // Check blacklist
            lock (_recentlyDeletedIds)
            {
                if (_recentlyDeletedIds.ContainsKey(newItem.Id)) continue;
            }

            if (existingMap.TryGetValue(newItem.Id, out var existing))
            {
                // We update properties directly on the existing object which is already in the Collection.
                // CopyTo triggers PropertyChanged events that must fire on the UI thread, so the
                // assignment itself is queued into the same batched UI dispatch as Add operations.
                var captured = newItem;
                var capturedExisting = existing;
                uiBatch.Add(() => captured.CopyTo(capturedExisting));
            }
            else
            {
                uiBatch.Add(() =>
                {
                    Collection.Add(newItem);
                    _idIndex[newItem.Id] = newItem;
                });
            }

            // Batch adding new items to keep UI smooth
            if (uiBatch.Count >= 50 || i == total - 1)
            {
                if (uiBatch.Count > 0)
                {
                    var currentBatch = uiBatch.ToList();
                    uiBatch.Clear();
                    await _uiDispatcher.InvokeAsync(() =>
                    {
                        foreach (var action in currentBatch) action();
                    });
                }

                status?.Report($"{UIUtils.GetLoc("sync.updating.metadata")}: {i + 1}/{total}");
                if (i < total - 1)
                {
                    await Task.Delay(1, ct);
                }
            }
        }
    }
}
