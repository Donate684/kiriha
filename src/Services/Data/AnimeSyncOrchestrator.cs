using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data.Repositories;
using Kiriha.Utils;
using Kiriha.Utils.UI;
using Serilog;
using Kiriha.Core.Infrastructure;

namespace Kiriha.Services.Data;

public class AnimeSyncOrchestrator
{
    private const int MinimumStatusGuardCount = 10;
    private const int MinimumStatusDropCount = 5;
    private const double MaximumAllowedStatusDropRatio = 0.30;

    private readonly AnimeRepository _animeRepository;
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly IEnumerable<ITrackerService> _trackers;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly RecognitionCache _recognitionCache;

    private int _syncing;
    public bool IsSyncing => Volatile.Read(ref _syncing) == 1;

    public AnimeSyncOrchestrator(
        AnimeRepository animeRepository,
        IUserAnimeRepository userAnimeRepo,
        IEnumerable<ITrackerService> trackers,
        IUiDispatcher uiDispatcher,
        RecognitionCache recognitionCache)
    {
        _animeRepository = animeRepository;
        _userAnimeRepo = userAnimeRepo;
        _trackers = trackers;
        _uiDispatcher = uiDispatcher;
        _recognitionCache = recognitionCache;
    }

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

            var currentItems = await _animeRepository.GetSnapshotAsync(new[] { MediaKind.Anime });
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * 0.7)
            {
                Log.Warning("SyncWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);
            
            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            var snapshot = await _animeRepository.GetSnapshotAsync(new[] { MediaKind.Anime });
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, new[] { MediaKind.Anime });
            
            await Task.Run(() => _recognitionCache.BuildIndex(_animeRepository.Collection));
            
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

            var kinds = new[] { MediaKind.Manga, MediaKind.LightNovel };
            var currentItems = await _animeRepository.GetSnapshotAsync(kinds);
            var localCount = currentItems.Count;
            if (localCount >= 50 && apiList.Count < localCount * 0.7)
            {
                Log.Warning("SyncMangaWithTrackers: aborting - incoming list ({Incoming}) is much smaller than local cache ({Local}). Likely a partial fetch.",
                    apiList.Count, localCount);
                return false;
            }

            if (!IsRemoteSnapshotSafe(currentItems, apiList))
                return false;

            await ProcessSyncResults(apiList, currentItems, status, ct);
            
            status?.Report(UIUtils.GetLoc("sync.saving.to_db"));
            var snapshot = await _animeRepository.GetSnapshotAsync(kinds);
            await _userAnimeRepo.SyncFromRemoteAsync(snapshot, kinds);
            
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
            if (local < MinimumStatusGuardCount) continue;

            var incoming = CountStatus(apiList, trackedStatus);
            var dropped = local - incoming;
            if (dropped < MinimumStatusDropCount) continue;

            var incomingRatio = (double)incoming / local;
            if (incomingRatio < MaximumAllowedStatusDropRatio)
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
        var existingMap = currentItems.ToDictionary(x => x.Id);

        var toRemove = currentItems.Where(x => !apiMap.ContainsKey(x.Id)).ToList();
        
        var uiBatch = new List<Action>();
        int total = apiList.Count;

        for (int i = 0; i < total; i++)
        {
            if (ct.IsCancellationRequested) break;
            
            var newItem = apiList[i];
            
            if (_animeRepository.IsRecentlyDeleted(newItem.Id)) continue;

            if (existingMap.TryGetValue(newItem.Id, out var existing))
            {
                var captured = newItem;
                var capturedExisting = existing;
                uiBatch.Add(() => captured.CopyTo(capturedExisting));
            }
            else
            {
                uiBatch.Add(() =>
                {
                    _animeRepository.AddToCollection(newItem);
                });
            }

            if (uiBatch.Count >= 50 || i == total - 1)
            {
                if (uiBatch.Count > 0)
                {
                    var currentBatch = uiBatch.ToList();
                    uiBatch.Clear();
                    await _animeRepository.ApplySyncBatchAsync(i == 49 || i == total - 1 && uiBatch.Count == total ? toRemove : new List<AnimeItem>(), currentBatch);
                    if (i < total - 1) toRemove.Clear(); // Only pass toRemove once
                }
                
                status?.Report($"{UIUtils.GetLoc("sync.updating.metadata")}: {i + 1}/{total}");
                if (i < total - 1)
                {
                    await Task.Delay(1, ct);
                }
            }
        }
        
        // If total == 0, still remove
        if (total == 0 && toRemove.Any())
        {
            await _animeRepository.ApplySyncBatchAsync(toRemove, uiBatch);
        }
    }
}
