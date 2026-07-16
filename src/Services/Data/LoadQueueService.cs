using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Services.AppLifecycle;

namespace Kiriha.Services.Data;

public class LoadQueueService : IDisposable
{
    private readonly ImageCacheService _imageCache;
    private readonly ShikiMetadataService _shikiMetadata;
    private readonly SettingsService _settings;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    private const int ImageWorkerCount = 5;
    private const int ShikiWorkerCount = 2;
    private const int ImageQueueCapacity = 512;
    private const int ShikiQueueCapacity = 256;

    private readonly object _dedupeLock = new();
    private readonly HashSet<int> _queuedForImage = new();
    private readonly HashSet<int> _queuedForShiki = new();
    private readonly Channel<AnimeItem> _imageQueue = CreateQueue(ImageQueueCapacity);
    private readonly Channel<AnimeItem> _shikiQueue = CreateQueue(ShikiQueueCapacity);

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

        for (int i = 0; i < ImageWorkerCount; i++)
        {
            _backgroundTasks.Run($"LoadQueueService.ImageWorker.{i + 1}", ImageWorkerLoopAsync);
        }

        for (int i = 0; i < ShikiWorkerCount; i++)
        {
            _backgroundTasks.Run($"LoadQueueService.ShikiWorker.{i + 1}", ShikiWorkerLoopAsync);
        }
    }

    public void EnqueueForViewport(IEnumerable<AnimeItem> items)
    {
        foreach (var item in items)
        {
            if (item == null) continue;

            bool needsImage = !string.IsNullOrEmpty(item.MainPictureUrl);
            bool needsShiki = (_settings.Current.UI.UseRussianTitles || _settings.Current.UI.UseRussianDescriptions) &&
                string.IsNullOrEmpty(item.RussianTitle);

            if (needsImage)
                _ = EnqueueAsync(item, _imageQueue.Writer, _queuedForImage);

            if (needsShiki)
                _ = EnqueueAsync(item, _shikiQueue.Writer, _queuedForShiki);
        }
    }

    public void ClearQueues()
    {
        lock (_dedupeLock)
        {
            while (_imageQueue.Reader.TryRead(out _)) { }
            while (_shikiQueue.Reader.TryRead(out _)) { }

            _queuedForImage.Clear();
            _queuedForShiki.Clear();
        }
    }

    private static Channel<AnimeItem> CreateQueue(int capacity)
    {
        return Channel.CreateBounded<AnimeItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    private async ValueTask EnqueueAsync(AnimeItem item, ChannelWriter<AnimeItem> writer, HashSet<int> queuedIds)
    {
        lock (_dedupeLock)
        {
            if (!queuedIds.Add(item.Id))
                return;
        }

        try
        {
            await writer.WriteAsync(item);
        }
        catch (ChannelClosedException)
        {
            lock (_dedupeLock)
            {
                queuedIds.Remove(item.Id);
            }
        }
    }

    private async Task ImageWorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var item in _imageQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await _imageCache.CacheBatchAsync(new[] { item }, ct: ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error pre-caching image in LoadQueueService");
            }
            finally
            {
                RemoveQueuedId(_queuedForImage, item.Id);
            }
        }
    }

    private async Task ShikiWorkerLoopAsync(CancellationToken ct)
    {
        await foreach (var item in _shikiQueue.Reader.ReadAllAsync(ct))
        {
            try
            {
                await _shikiMetadata.LocalizeItemsAsync(new[] { item }, null, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Error processing shiki queue in LoadQueueService");
            }
            finally
            {
                RemoveQueuedId(_queuedForShiki, item.Id);
            }
        }
    }

    private void RemoveQueuedId(HashSet<int> queuedIds, int id)
    {
        lock (_dedupeLock)
        {
            queuedIds.Remove(id);
        }
    }

    public void Dispose()
    {
        _imageQueue.Writer.TryComplete();
        _shikiQueue.Writer.TryComplete();
    }
}
