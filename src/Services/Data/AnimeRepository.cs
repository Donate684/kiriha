using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data.Repositories;
using Kiriha.Utils.Collections;
using Serilog;

namespace Kiriha.Services.Data;

public class AnimeRepository
{
    private readonly IUserAnimeRepository _userAnimeRepo;
    private readonly DatabaseInitializer _dbInit;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly RecognitionCache _recognitionCache;

    private readonly TaskCompletionSource _initTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _initStarted;
    private readonly Dictionary<int, CancellationTokenSource> _recentlyDeletedIds = new();
    private readonly Dictionary<int, AnimeItem> _idIndex = new();

    public Task InitializationTask => _initTcs.Task;
    public bool IsInitializing => Volatile.Read(ref _initStarted) == 1 && !_initTcs.Task.IsCompleted;

    public BulkObservableCollection<AnimeItem> Collection { get; } = new();

    public AnimeRepository(
        IUserAnimeRepository userAnimeRepo,
        DatabaseInitializer dbInit,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher,
        RecognitionCache recognitionCache)
    {
        _userAnimeRepo = userAnimeRepo;
        _dbInit = dbInit;
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _recognitionCache = recognitionCache;
    }

    public bool IsRecentlyDeleted(int animeId)
    {
        lock (_recentlyDeletedIds) return _recentlyDeletedIds.ContainsKey(animeId);
    }

    public async Task InitializeAsync()
    {
        if (Interlocked.CompareExchange(ref _initStarted, 1, 0) != 0)
        {
            await _initTcs.Task;
            return;
        }

        try
        {
            var total = Stopwatch.StartNew();
            var stage = Stopwatch.StartNew();
            await _dbInit.InitializationTask;
            Log.Information("StartupTiming: anime repo waited for database elapsedMs={ElapsedMs}", stage.ElapsedMilliseconds);

            stage.Restart();
            var cached = await _userAnimeRepo.GetAllAsync();
            Log.Information(
                "StartupTiming: cached anime loaded count={Count} elapsedMs={ElapsedMs}",
                cached?.Count ?? 0,
                stage.ElapsedMilliseconds);

            stage.Restart();
            await _uiDispatcher.InvokeAsync(() =>
            {
                if (cached != null && cached.Count > 0)
                {
                    Collection.Reset(cached);
                    _idIndex.Clear();
                    foreach (var item in cached) _idIndex[item.Id] = item;
                }
                else
                {
                    Collection.Clear();
                    _idIndex.Clear();
                }
            });

            await Task.Run(() => _recognitionCache.BuildIndex(Collection));

            Log.Information(
                "StartupTiming: cached anime applied to UI collection count={Count} elapsedMs={ElapsedMs}",
                Collection.Count,
                stage.ElapsedMilliseconds);

            Log.Information("StartupTiming: anime repo initialized elapsedMs={ElapsedMs}", total.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize AnimeRepository");
        }
        finally
        {
            _initTcs.TrySetResult();
        }
    }

    public async Task AddOrUpdateAnimeAsync(AnimeItem item)
    {
        lock (_recentlyDeletedIds)
        {
            if (_recentlyDeletedIds.TryGetValue(item.Id, out var cts))
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
                _recentlyDeletedIds.Remove(item.Id);
            }
        }

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

    public async Task RemoveAnimeLocalAsync(int animeId)
    {
        var newCts = new CancellationTokenSource();
        lock (_recentlyDeletedIds)
        {
            if (_recentlyDeletedIds.TryGetValue(animeId, out var oldCts))
            {
                try { oldCts.Cancel(); } catch (ObjectDisposedException) { }
            }
            _recentlyDeletedIds[animeId] = newCts;
        }

        _ = _backgroundTasks.Run("AnimeRepository.RecentDeleteExpiry", async ct =>
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), linkedCts.Token);
            }
            catch (OperationCanceledException) { }
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
                _idIndex.Remove(animeId);
            }
        });

        await _userAnimeRepo.DeleteAsync(animeId);
    }

    public async Task ApplySyncBatchAsync(List<AnimeItem> toRemove, List<Action> uiBatch)
    {
        if (toRemove.Any())
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var item in toRemove)
                {
                    Collection.Remove(item);
                    _idIndex.Remove(item.Id);
                }
            });
        }

        if (uiBatch.Any())
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                foreach (var action in uiBatch) action();
            });
        }
    }

    public Task<Dictionary<int, AnimeItem>> GetExistingMapAsync(MediaKind[] kinds)
    {
        return _uiDispatcher.InvokeAsync(() =>
            Collection.Where(x => kinds.Contains(x.MediaKind)).ToDictionary(x => x.Id));
    }

    public Task<List<AnimeItem>> GetSnapshotAsync(MediaKind[] kinds)
    {
        return _uiDispatcher.InvokeAsync(() =>
            Collection.Where(x => kinds.Contains(x.MediaKind)).ToList());
    }

    public void AddToCollection(AnimeItem item)
    {
        Collection.Add(item);
        _idIndex[item.Id] = item;
    }
}
