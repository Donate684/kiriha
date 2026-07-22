using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Services.Data;

public class ImageDownloader : IDisposable
{
    private readonly string _cacheRoot;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _downloadSemaphore = new(6, 6);
    private readonly ConcurrentDictionary<string, Task<string>> _activeDownloads = new();

    public ImageDownloader(IHttpClientFactory httpClientFactory, string cacheRoot)
    {
        _httpClientFactory = httpClientFactory;
        _cacheRoot = cacheRoot;
    }

    public Task<string> GetLocalPathOrDownload(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(url)) return Task.FromResult(string.Empty);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var addedTask = _activeDownloads.GetOrAdd(url, tcs.Task);

        if (ReferenceEquals(addedTask, tcs.Task))
        {
            _ = ExecuteDownloadAsync(url, tcs, ct);
        }

        return addedTask;
    }

    private async Task ExecuteDownloadAsync(string url, TaskCompletionSource<string> tcs, CancellationToken ct = default)
    {
        try
        {
            var result = string.Empty;
            try
            {
                result = await DownloadCoreAsync(url, ct);
            }
            catch (Exception)
            {
                result = string.Empty;
            }
            tcs.SetResult(result);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
        finally
        {
            // Always remove from active downloads unconditionally.
            // It only serves as a concurrent deduplicator.
            _activeDownloads.TryRemove(url, out _);
        }
    }

    private async Task<string> DownloadCoreAsync(string url, CancellationToken ct = default)
    {
        string fileName = GetHashString(url) + Path.GetExtension(url.Split('?')[0]);
        if (string.IsNullOrEmpty(Path.GetExtension(fileName))) fileName += ".jpg";

        string localPath = Path.Combine(_cacheRoot, fileName);
        string tmpPath = localPath + ".tmp";

        if (File.Exists(localPath))
        {
            var fileInfo = new FileInfo(localPath);
            if (fileInfo.Length > 0) return localPath;
            try { fileInfo.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete old file {FilePath}", localPath); }
        }

        int retryCount = 2;
        for (int i = 0; i <= retryCount; i++)
        {
            try
            {
                await _downloadSemaphore.WaitAsync(ct);
                try
                {
                    if (File.Exists(localPath))
                    {
                        var fileInfo = new FileInfo(localPath);
                        if (fileInfo.Length > 0) return localPath;
                        try { fileInfo.Delete(); } catch (Exception ex) { Log.Debug(ex, "Failed to delete file {FilePath} on collision", localPath); }
                    }

                    if (File.Exists(tmpPath))
                    {
                        try { File.Delete(tmpPath); } catch (Exception ex) { Log.Debug(ex, "Failed to delete temp file {TmpPath} after copy error", tmpPath); }
                    }

                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(30));
                    try
                    {
                        using var client = _httpClientFactory.CreateClient("ImageClient");
                        var bytes = await client.GetByteArrayAsync(url, cts.Token);
                        await File.WriteAllBytesAsync(tmpPath, bytes, cts.Token);

                        File.Move(tmpPath, localPath, true);
                        return localPath;
                    }
                    catch
                    {
                        if (File.Exists(tmpPath))
                        {
                            try { File.Delete(tmpPath); } catch (Exception ex) { Log.Debug(ex, "Failed to delete temp file {TmpPath} in finally", tmpPath); }
                        }
                        throw;
                    }
                }
                finally
                {
                    _downloadSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                if (i == retryCount)
                {
                    Log.Error(ex, "Failed to download image after {Retry} retries: {Url}", retryCount, url);
                    return string.Empty;
                }
                await Task.Delay(1000 * (i + 1), ct); // Exponential-ish backoff
            }
        }

        return string.Empty;
    }

    public static string GetHashString(string inputString)
    {
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(inputString));
        return Convert.ToHexString(hashBytes);
    }

    public void Dispose()
    {
        _downloadSemaphore.Dispose();
    }
}
