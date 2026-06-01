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
using Avalonia;
using Avalonia.Media.Imaging;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.AppLifecycle;
using Serilog;

namespace Kiriha.Services.Data;

public class ImageCacheService : IDisposable
{
    private readonly string CacheRoot = Kiriha.Core.PathHelper.GetImageCachePath();

    private readonly HttpClient _client;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    private const long MaxDiskCacheSizeBytes = 1024L * 1024 * 1024; // 1 GB
    private readonly SemaphoreSlim _downloadSemaphore = new(6, 6);
    private readonly SemaphoreSlim _decodeSemaphore = new(12, 12);

    // Two-tier in-memory cache: 16 MB pre-decoded pixels (~30 posters) on top
    // of 32 MB encoded JPEG bytes (~400-1000 posters). See BitmapMemoryCache
    // for the full rationale and why each caller still gets its own Bitmap.
    private readonly BitmapMemoryCache _memCache = new(
        encodedBudgetBytes: 32L * 1024 * 1024,
        pixelBudgetBytes:   16L * 1024 * 1024);
    
    public ImageCacheService(IHttpClientFactory httpClientFactory, IBackgroundTaskSupervisor backgroundTasks)
    {
        _backgroundTasks = backgroundTasks;
        _client = httpClientFactory.CreateClient();
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(Kiriha.Core.AppInfo.UserAgent);

        if (!Directory.Exists(CacheRoot))
        {
            Directory.CreateDirectory(CacheRoot);
        }
        else
        {
            _backgroundTasks.Run("ImageCacheService.Cleanup", _ => CleanupCacheIfNeededAsync());
        }
    }

    public async Task<Bitmap?> LoadBitmapAsync(string url, int decodeWidth = 300)
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
            string fileName = GetHashString(url) + Path.GetExtension(url.Split('?')[0]);
            if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".jpg";
            string candidatePath = Path.Combine(CacheRoot, fileName);

            if (File.Exists(candidatePath))
            {
                var fileInfo = new FileInfo(candidatePath);
                if (fileInfo.Length > 0)
                {
                    try { fileInfo.LastWriteTime = DateTime.Now; } catch { }
                    localPath = candidatePath;
                }
                else
                {
                    try { fileInfo.Delete(); } catch { }
                    localPath = await GetLocalPathOrDownload(url);
                }
            }
            else
            {
                localPath = await GetLocalPathOrDownload(url);
            }
        }

        if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath)) return null;

        // L1 fast path: pre-decoded pixels rented as a fresh WriteableBitmap.
        // Each caller gets an INDEPENDENT instance Ã¢â‚¬â€ required because
        // AsyncImageLoader.AdvancedImage disposes the "previous" Source on
        // rebind (recycling in ItemsRepeater), so a shared bitmap would die
        // on neighbour cards. See BitmapMemoryCache for full rationale.
        if (_memCache.TryRentBitmap(localPath, decodeWidth, out var rented) && rented != null)
            return rented;

        await _decodeSemaphore.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                // Re-check L1 Ã¢â‚¬â€ another caller may have populated it while we
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

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _activeDownloads = new();

    public Task<string> GetLocalPathOrDownload(string url)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult(string.Empty);
        
        var lazy = _activeDownloads.GetOrAdd(url, u => new Lazy<Task<string>>(async () => 
        {
            var result = string.Empty;
            try
            {
                result = await DownloadCoreAsync(u);
            }
            catch (Exception)
            {
                result = string.Empty;
            }
            finally
            {
                // Always remove from active downloads unconditionally.
                // It only serves as a concurrent deduplicator.
                _activeDownloads.TryRemove(u, out _);
            }
            return result;
        }));
        return lazy.Value;
    }

    private async Task<string> DownloadCoreAsync(string url)
    {
        string fileName = GetHashString(url) + Path.GetExtension(url.Split('?')[0]);
        if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".jpg";
        
        string localPath = Path.Combine(CacheRoot, fileName);
        string tmpPath = localPath + ".tmp";

        if (File.Exists(localPath))
        {
            var fileInfo = new FileInfo(localPath);
            if (fileInfo.Length > 0) return localPath;
            try { fileInfo.Delete(); } catch { }
        }

        int retryCount = 2;
        for (int i = 0; i <= retryCount; i++)
        {
            try
            {
                await _downloadSemaphore.WaitAsync();
                try
                {
                    if (File.Exists(localPath))
                    {
                        var fileInfo = new FileInfo(localPath);
                        if (fileInfo.Length > 0) return localPath;
                        try { fileInfo.Delete(); } catch { }
                    }

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var bytes = await _client.GetByteArrayAsync(url, cts.Token);
                    
                    await File.WriteAllBytesAsync(tmpPath, bytes, cts.Token);
                    
                    if (File.Exists(tmpPath))
                    {
                        File.Move(tmpPath, localPath, true);
                        return localPath;
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                // Cleanup partial temp files
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }

                if (i == retryCount)
                {
                    Log.Error(ex, "Failed to download image after {Retry} retries: {Url}", retryCount, url);
                    return string.Empty;
                }
                await Task.Delay(1000 * (i + 1)); // Exponential-ish backoff
            }
        }
        
        return string.Empty;
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
                            catch { /* file may have been removed concurrently */ }
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
            long totalSize = directoryInfo.EnumerateFiles().Sum(f => f.Length);

            if (totalSize > MaxDiskCacheSizeBytes)
            {
                long targetSize = (long)(MaxDiskCacheSizeBytes * 0.7);
                var filesToDelete = directoryInfo.EnumerateFiles().OrderBy(f => f.LastWriteTime).ToList();

                foreach (var file in filesToDelete)
                {
                    if (totalSize <= targetSize) break;
                    long len = file.Length;
                    try
                    {
                        file.Delete();
                        totalSize -= len;
                    }
                    catch
                    {
                        // File locked (antivirus, in-use). Skip and keep evicting older files
                        // \u2014 we don't want a single locked file to abort the whole LRU pass.
                    }
                }
            }
        }
        catch { }
    }

    private string GetHashString(string inputString)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        return Convert.ToHexString(hashBytes);
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
            var localPath = await GetLocalPathOrDownload(item.MainPictureUrl!);
            if (string.IsNullOrEmpty(localPath) || ct.IsCancellationRequested)
                return;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                item.LocalPosterPath = localPath;
            });
            onProgress?.Invoke(increment(), total);
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _downloadSemaphore.Dispose();
        _decodeSemaphore.Dispose();
        // NOTE: _client comes from IHttpClientFactory.CreateClient(); the factory
        // owns the underlying HttpMessageHandler lifetime. Disposing it here is
        // an antipattern that can lead to ObjectDisposedException on shared handlers.
    }
}
