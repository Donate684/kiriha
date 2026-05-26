using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;

namespace Kiriha.Services.Data;

public class LoadQueueService : IDisposable
{
    private readonly ImageCacheService _imageCache;
    private readonly ShikiMetadataService _shikiMetadata;
    private readonly SettingsService _settings;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    
    private readonly object _lock = new();
    private readonly Queue<AnimeItem> _imageQueue = new();
    private readonly Queue<AnimeItem> _shikiQueue = new();
    private readonly HashSet<int> _queuedForImage = new();
    private readonly HashSet<int> _queuedForShiki = new();

    private readonly SemaphoreSlim _imageWorkers = new(5, 5);
    private readonly SemaphoreSlim _shikiWorkers = new(2, 2);
    private readonly SemaphoreSlim _queueSignal = new(0);

    public LoadQueueService(
        ImageCacheService imageCache,
        ShikiMetadataService shikiMetadata,
        SettingsService settings,
        IBackgroundTaskSupervisor backgroundTasks)
    {
        _imageCache = imageCache;
        _shikiMetadata = shikiMetadata;
        _settings = settings;
        _backgroundTasks = backgroundTasks;
        _backgroundTasks.Run("LoadQueueService.ProcessQueues", ProcessQueuesLoopAsync);
    }

    public void EnqueueForViewport(IEnumerable<AnimeItem> items)
    {
        lock (_lock)
        {
            bool added = false;
            foreach (var item in items)
            {
                if (item == null) continue;
                
                bool needsImage = !string.IsNullOrEmpty(item.MainPictureUrl);
                bool needsShiki = (_settings.Current.UI.UseRussianTitles || _settings.Current.UI.UseRussianDescriptions) && 
                    string.IsNullOrEmpty(item.RussianTitle);

                if (needsImage && _queuedForImage.Add(item.Id))
                {
                    _imageQueue.Enqueue(item);
                    added = true;
                }

                if (needsShiki && _queuedForShiki.Add(item.Id))
                {
                    _shikiQueue.Enqueue(item);
                    added = true;
                }
            }
            
            if (added)
            {
                _queueSignal.Release();
            }
        }
    }

    public void ClearQueues()
    {
        lock (_lock)
        {
            _imageQueue.Clear();
            _shikiQueue.Clear();
            _queuedForImage.Clear();
            _queuedForShiki.Clear();
        }
    }

    private async Task ProcessQueuesLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                AnimeItem? nextImage = null;
                AnimeItem? nextShiki = null;

                // Pull at most one task per source per loop tick. We don't gate on
                // SemaphoreSlim.CurrentCount here Ã¢â‚¬â€ that value isn't decremented until
                // the worker actually awaits WaitAsync inside the spawned Task.Run, so a
                // tight loop iteration could dequeue more items than the worker pool size.
                // The semaphore inside each worker still enforces the real concurrency cap.
                lock (_lock)
                {
                    if (_imageQueue.Count > 0)
                    {
                        nextImage = _imageQueue.Dequeue();
                    }
                    if (_shikiQueue.Count > 0)
                    {
                        nextShiki = _shikiQueue.Dequeue();
                    }
                }

                if (nextImage == null && nextShiki == null)
                {
                    await _queueSignal.WaitAsync(2000, ct);
                    continue;
                }
                else
                {
                    // Attempt to consume a signal token so it doesn't grow infinitely
                    _queueSignal.Wait(0);
                }

                if (nextImage != null)
                {
                    _ = _backgroundTasks.Run("LoadQueueService.ImageWorker", async workerCt =>
                    {
                        await _imageWorkers.WaitAsync(workerCt);
                        try
                        {
                            await _imageCache.CacheBatchAsync(new[] { nextImage }, ct: workerCt);
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "Error pre-caching image in LoadQueueService");
                        }
                        finally
                        {
                            _imageWorkers.Release();
                            lock (_lock) { _queuedForImage.Remove(nextImage.Id); }
                            _queueSignal.Release();
                        }
                    }, ct);
                }

                if (nextShiki != null)
                {
                    _ = _backgroundTasks.Run("LoadQueueService.ShikiWorker", async workerCt =>
                    {
                        await _shikiWorkers.WaitAsync(workerCt);
                        try
                        {
                            await _shikiMetadata.LocalizeItemsAsync(new[] { nextShiki }, null, workerCt);
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Error(ex, "Error processing shiki queue in LoadQueueService");
                        }
                        finally
                        {
                            _shikiWorkers.Release();
                            lock (_lock) { _queuedForShiki.Remove(nextShiki.Id); }
                            _queueSignal.Release();
                        }
                    }, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Unhandled exception in ProcessQueuesLoopAsync");
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch { /* Ignore TaskCanceledException */ }
            }
        }
    }

    public void Dispose()
    {
        _queueSignal.Dispose();
        _imageWorkers.Dispose();
        _shikiWorkers.Dispose();
    }
}
