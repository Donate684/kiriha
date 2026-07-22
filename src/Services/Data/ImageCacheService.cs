using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data;

public class ImageCacheService : IDisposable
{
    private readonly string CacheRoot = Kiriha.Core.Platform.PathHelper.GetImageCachePath();

    private readonly IBackgroundTaskSupervisor _backgroundTasks;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ImageDownloader _downloader;

    private const long MaxDiskCacheSizeBytes = 1024L * 1024 * 1024; // 1 GB
    private readonly SemaphoreSlim _decodeSemaphore = new(12, 12);

    // Two-tier in-memory cache: 16 MB pre-decoded pixels (~30 posters) on top
    // of 32 MB encoded JPEG bytes (~400-1000 posters). See BitmapMemoryCache
    // for the full rationale and why each caller still gets its own Bitmap.
    private readonly BitmapMemoryCache _memCache = new(
        encodedBudgetBytes: 32L * 1024 * 1024,
        pixelBudgetBytes: 16L * 1024 * 1024);

    public ImageCacheService(
        IHttpClientFactory httpClientFactory,
        IBackgroundTaskSupervisor backgroundTasks,
        IUiDispatcher uiDispatcher)
    {
        _backgroundTasks = backgroundTasks;
        _uiDispatcher = uiDispatcher;
        _downloader = new ImageDownloader(httpClientFactory, CacheRoot);

        if (!Directory.Exists(CacheRoot))
        {
            Directory.CreateDirectory(CacheRoot);
        }
        else
        {
            _backgroundTasks.Run("ImageCacheService.Cleanup", _ => CleanupCacheIfNeededAsync());
        }
    }

    public async Task<Bitmap?> LoadBitmapAsync(string url, int decodeWidth = 300, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return null;

        // Resolve local path (download if necessary).
        string localPath = string.Empty;
        bool isUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        if (!isUrl && File.Exists(url))
        {
            localPath = url;
        }
        else if (isUrl)
        {
            string fileName = ImageDownloader.GetHashString(url) + Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".jpg";
            string candidatePath = Path.Combine(CacheRoot, fileName);

            if (File.Exists(candidatePath))
            {
                var fileInfo = new FileInfo(candidatePath);
                if (fileInfo.Length > 0)
                {
                    try { fileInfo.LastWriteTime = DateTime.Now; } catch (Exception ex) { Log.Debug(ex, "Failed to update LastWriteTime for {FilePath}", localPath); }
                    localPath = candidatePath;
                }
                else
                {
                    try { fileInfo.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete corrupted file {FilePath}", localPath); }
                    localPath = await _downloader.GetLocalPathOrDownload(url, ct);
                }
            }
            else
            {
                localPath = await _downloader.GetLocalPathOrDownload(url, ct);
            }
        }

        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return null;

        // L1 fast path: pre-decoded pixels rented as a fresh WriteableBitmap.
        // Each caller gets an INDEPENDENT instance — required because
        // AsyncImageLoader.AdvancedImage disposes the "previous" Source on
        // rebind (recycling in ItemsRepeater), so a shared bitmap would die
        // on neighbour cards. See BitmapMemoryCache for full rationale.
        if (_memCache.TryRentBitmap(localPath, decodeWidth, out var rented) && rented != null)
            return rented;

        await _decodeSemaphore.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                // Re-check L1 — another caller may have populated it while we
                // waited on the decode semaphore.
                if (_memCache.TryRentBitmap(localPath, decodeWidth, out var rented2) && rented2 != null)
                    return rented2;

                try
                {
                    // L2: encoded bytes. Avoids the File.OpenRead syscall on hits.
                    if (!_memCache.TryGetEncoded(localPath, out var bytes) || bytes == null)
                    {
                        bytes = File.ReadAllBytes(localPath);
                        _memCache.StoreEncoded(localPath, bytes);
                    }

                    using var ms = new MemoryStream(bytes, writable: false);
                    var bmp = Bitmap.DecodeToWidth(ms, decodeWidth);

                    // Promote pixels to L1 so subsequent renders skip the decode entirely.
                    _memCache.StorePixelsFrom(localPath, decodeWidth, bmp);
                    return bmp;
                }
                catch (Exception ex)
                {
                    Log.Debug("Failed to decode bitmap {Url}: {Msg}", url, ex.Message);
                    return null;
                }
            });
        }
        finally
        {
            _decodeSemaphore.Release();
        }
    }



    public async Task PerformSmartCleanupAsync(IEnumerable<string> activePaths)
    {
        try
        {
            var activeSet = activePaths
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => Path.GetFileName(p).ToLowerInvariant())
                .ToHashSet();

            var directoryInfo = new DirectoryInfo(CacheRoot);
            int deletedCount = 0;
            long reclaimedSpace = 0;
            var threshold = DateTime.Now.AddDays(-7);

            await Task.Run(() =>
            {
                // Materialize the snapshot first; deleting during EnumerateFiles
                // is undefined on NTFS and can skip files or throw.
                var snapshot = directoryInfo.EnumerateFiles().ToList();
                foreach (var file in snapshot)
                {
                    if (!activeSet.Contains(file.Name.ToLowerInvariant()))
                    {
                        // Only delete non-active files if they are older than 7 days
                        if (file.LastWriteTime < threshold)
                        {
                            try
                            {
                                long len = file.Length;
                                file.Delete();
                                reclaimedSpace += len;
                                deletedCount++;
                            }
                            catch (Exception ex) { Log.Debug(ex, "File may have been removed concurrently: {FilePath}", file.FullName); }
                        }
                    }
                }
            });

            if (deletedCount > 0)
                Log.Information("ImageCacheService: Cleaned {Count} unreferenced old images, reclaimed {Space:N2} MB",
                    deletedCount, reclaimedSpace / 1024.0 / 1024.0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ImageCacheService: Error during smart cleanup");
        }
    }

    public void ClearMemoryCache() => _memCache.Clear();

    private async Task CleanupCacheIfNeededAsync()
    {
        try
        {
            var directoryInfo = new DirectoryInfo(CacheRoot);
            var files = directoryInfo.EnumerateFiles().ToList();
            long totalSize = files.Sum(f => f.Length);

            if (totalSize > MaxDiskCacheSizeBytes)
            {
                long targetSize = (long)(MaxDiskCacheSizeBytes * 0.7);
                var filesToDelete = files.OrderBy(f => f.LastWriteTime).ToList();

                foreach (var file in filesToDelete)
                {
                    if (totalSize <= targetSize) break;
                    long len = file.Length;
                    try
                    {
                        file.Delete();
                        totalSize -= len;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to delete file {FilePath} during cleanup", file.FullName);
                        // File locked (antivirus, in-use). Skip and keep evicting older files
                        // — we don't want a single locked file to abort the whole LRU pass.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cache cleanup failed");
        }
    }



    public async Task CacheBatchAsync(IEnumerable<AnimeItem> items, Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        var toDownload = items.Where(NeedsPosterDownload).ToList();

        if (toDownload.Count == 0) return;

        int count = 0;
        if (toDownload.Count == 1)
        {
            await CachePosterAsync(toDownload[0], toDownload.Count, onProgress, () => Interlocked.Increment(ref count), ct);
            return;
        }

        var tasks = toDownload.Select(async item =>
        {
            await CachePosterAsync(item, toDownload.Count, onProgress, () => Interlocked.Increment(ref count), ct);
        });

        await Task.WhenAll(tasks);
    }

    private static bool NeedsPosterDownload(AnimeItem item)
    {
        if (string.IsNullOrEmpty(item.MainPictureUrl))
            return false;

        if (string.IsNullOrEmpty(item.LocalPosterPath) || !File.Exists(item.LocalPosterPath))
            return true;

        return new FileInfo(item.LocalPosterPath).Length == 0;
    }

    private async Task CachePosterAsync(
        AnimeItem item,
        int total,
        Action<int, int>? onProgress,
        Func<int> increment,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var localPath = await _downloader.GetLocalPathOrDownload(item.MainPictureUrl!, ct);
            if (string.IsNullOrEmpty(localPath) || ct.IsCancellationRequested)
                return;

            _uiDispatcher.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                item.LocalPosterPath = localPath;
            });
            onProgress?.Invoke(increment(), total);
        }
        catch (OperationCanceledException) { }
    }

    public Task<string> GetLocalPathOrDownload(string url, CancellationToken ct = default)
    {
        return _downloader.GetLocalPathOrDownload(url, ct);
    }

    public void Dispose()
    {
        _downloader.Dispose();
        _decodeSemaphore.Dispose();
        _memCache.Clear();
    }
}
