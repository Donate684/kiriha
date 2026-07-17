using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core.Mpv;

public sealed class MpvThumbnailer : IDisposable
{
    private const int MaxCacheItems = 80;
    private const double CacheStepSeconds = 2.0;
    private const int ThumbnailWidth = 640;
    private const int ThumbnailHeight = 360;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _captureGate = new(1, 1);
    private readonly string _thumbnailDirectory;
    private readonly FileStream _lockFile;
    private readonly Dictionary<int, CacheEntry> _cache = new();
    private IntPtr _handle;
    private string? _loadedPath;
    private bool _disposed;
    private int _activeCalls;

    static MpvThumbnailer()
    {
        Task.Run(() =>
        {
            try
            {
                var baseDir = Path.Combine(Path.GetTempPath(), "Kiriha", "timeline-thumbs");
                if (Directory.Exists(baseDir))
                {
                    foreach (var dir in Directory.GetDirectories(baseDir))
                    {
                        try
                        {
                            var lockFilePath = Path.Combine(dir, ".lock");
                            bool isLocked = false;

                            if (File.Exists(lockFilePath))
                            {
                                try
                                {
                                    using var fs = new FileStream(lockFilePath, FileMode.Open, FileAccess.Write, FileShare.None);
                                }
                                catch (IOException)
                                {
                                    isLocked = true;
                                }
                            }
                            else if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) < TimeSpan.FromSeconds(10))
                            {
                                isLocked = true;
                            }

                            if (!isLocked)
                            {
                                Directory.Delete(dir, recursive: true);
                            }
                        }
                        catch (Exception ex) { Log.Debug(ex, "Failed to clean up thumbnail directory: {Dir}", dir); }
                    }
                }
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to enumerate root thumbnail directory"); }
        });
    }

    public MpvThumbnailer()
    {
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), "Kiriha", "timeline-thumbs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_thumbnailDirectory);
        _lockFile = new FileStream(Path.Combine(_thumbnailDirectory, ".lock"), FileMode.Create, FileAccess.Write, FileShare.Read);

        _handle = LibMpvNative.mpv_create();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create mpv thumbnailer instance.");

        SetOption("config", "no");
        SetOption("terminal", "no");
        SetOption("msg-level", "all=no");
        SetOption("idle", "yes");
        SetOption("pause", "yes");
        SetOption("keep-open", "yes");
        SetOption("osc", "no");
        SetOption("input-default-bindings", "no");
        SetOption("input-vo-keyboard", "no");
        SetOption("aid", "no");
        SetOption("sid", "no");
        SetOption("ytdl", "no");
        SetOption("hwdec", "no");
        SetOption("vo", "null");
        SetOption("force-window", "no");
        SetOption("hr-seek", "no");
        SetOption("vd-lavc-skiploopfilter", "all");
        SetOption("vd-lavc-fast", "yes");
        SetOption("vd-lavc-threads", "2");
        SetOption("sws-scaler", "bicubic");
        SetOption("vf", $"lavfi=[scale=w={ThumbnailWidth}:h={ThumbnailHeight}:force_original_aspect_ratio=decrease:flags=bicubic,pad=w={ThumbnailWidth}:h={ThumbnailHeight}:x=(ow-iw)/2:y=(oh-ih)/2]");
        SetOption("demuxer-max-bytes", "16MiB");
        SetOption("demuxer-max-back-bytes", "4MiB");
        SetOption("screenshot-format", "jpg");
        SetOption("screenshot-jpeg-quality", "90");

        Check(LibMpvNative.mpv_initialize(_handle), "initialize mpv thumbnailer");
    }

    public async Task<string?> GetThumbnailAsync(string videoPath, double timeSeconds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return null;

        var bucket = ToBucket(timeSeconds);
        lock (_gate)
        {
            if (_cache.TryGetValue(bucket, out var cached) && File.Exists(cached.Path))
            {
                cached.LastUsedUtc = DateTime.UtcNow;
                return cached.Path;
            }
        }

        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(bucket, out var cached) && File.Exists(cached.Path))
                {
                    cached.LastUsedUtc = DateTime.UtcNow;
                    return cached.Path;
                }
            }

            return await Task.Run(() => CaptureThumbnail(videoPath, bucket, cancellationToken), cancellationToken);
        }
        finally
        {
            _captureGate.Release();
        }
    }

    public Task WarmUpAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return Task.CompletedTask;

        return Task.Run(async () =>
        {
            await _captureGate.WaitAsync(cancellationToken);
            try
            {
                if (!TryEnterActiveCall(out var handle))
                    return;

                try
                {
                    EnsureLoaded(handle, videoPath, cancellationToken);
                }
                finally
                {
                    LeaveActiveCall();
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            finally
            {
                _captureGate.Release();
            }
        }, cancellationToken);
    }

    private bool TryEnterActiveCall(out IntPtr handle)
    {
        lock (_gate)
        {
            if (_disposed || _handle == IntPtr.Zero)
            {
                handle = IntPtr.Zero;
                return false;
            }
            _activeCalls++;
            handle = _handle;
            return true;
        }
    }

    private void LeaveActiveCall()
    {
        lock (_gate)
        {
            _activeCalls--;
            if (_disposed && _activeCalls == 0 && _handle != IntPtr.Zero)
            {
                LibMpvNative.mpv_terminate_destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    public static int GetCacheBucket(double timeSeconds) => ToBucket(timeSeconds);

    private string? CaptureThumbnail(string videoPath, int bucket, CancellationToken cancellationToken)
    {
        if (!TryEnterActiveCall(out var handle))
            return null;

        try
        {
            EnsureLoaded(handle, videoPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = Path.Combine(_thumbnailDirectory, $"thumb-{bucket:000000}.jpg");
            TryDelete(targetPath);

            var seconds = (bucket * CacheStepSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            Check(LibMpvNative.mpv_command_string(handle, "seek", seconds, "absolute+keyframes"), "seek thumbnailer");
            
            if (cancellationToken.WaitHandle.WaitOne(45))
                cancellationToken.ThrowIfCancellationRequested();

            Check(LibMpvNative.mpv_command_string(handle, "screenshot-to-file", targetPath, "video"), "capture thumbnail");
            if (!WaitForFile(targetPath, cancellationToken))
                return null;

            lock (_gate)
            {
                _cache[bucket] = new CacheEntry(targetPath);
                TrimCache();
            }
            return targetPath;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to generate timeline thumbnail");
            return null;
        }
        finally
        {
            LeaveActiveCall();
        }
    }

    private void EnsureLoaded(IntPtr handle, string videoPath, CancellationToken cancellationToken)
    {
        bool needsLoad = false;
        lock (_gate)
        {
            if (!string.Equals(_loadedPath, videoPath, StringComparison.Ordinal))
            {
                _cache.Clear();
                _loadedPath = videoPath;
                needsLoad = true;
            }
        }

        if (!needsLoad)
            return;

        Check(LibMpvNative.mpv_command_string(handle, "loadfile", videoPath, "replace"), "load thumbnail source");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool loaded = false;
        while (sw.ElapsedMilliseconds < 3000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eventPtr = LibMpvNative.mpv_wait_event(handle, 0.01);
            if (eventPtr != IntPtr.Zero)
            {
                var mpvEvent = Marshal.PtrToStructure<MpvEvent>(eventPtr);
                if (mpvEvent.EventId == LibMpvNative.MPV_EVENT_FILE_LOADED)
                {
                    loaded = true;
                    break;
                }
                
                if (mpvEvent.EventId == LibMpvNative.MPV_EVENT_END_FILE)
                {
                    var endFile = Marshal.PtrToStructure<MpvEventEndFile>(mpvEvent.Data);
                    if (endFile.Reason == MpvPlaybackEndedEventArgs.ReasonError)
                    {
                        Log.Warning("Thumbnailer failed to load: {VideoPath}", videoPath);
                        break;
                    }
                }
            }
        }

        if (!loaded)
        {
            lock (_gate)
            {
                if (string.Equals(_loadedPath, videoPath, StringComparison.Ordinal))
                    _loadedPath = null;
            }
        }
    }

    private void TrimCache()
    {
        if (_cache.Count <= MaxCacheItems)
            return;

        foreach (var pair in _cache.OrderBy(x => x.Value.LastUsedUtc).Take(_cache.Count - MaxCacheItems).ToArray())
        {
            _cache.Remove(pair.Key);
            TryDelete(pair.Value.Path);
        }
    }

    private static int ToBucket(double timeSeconds)
    {
        return Math.Max(0, (int)Math.Round(timeSeconds / CacheStepSeconds));
    }

    private static bool WaitForFile(string path, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 20; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return true;
            
            if (cancellationToken.WaitHandle.WaitOne(25))
                cancellationToken.ThrowIfCancellationRequested();
        }

        return false;
    }

    private void SetOption(string name, string value)
    {
        Check(LibMpvNative.mpv_set_option_string(_handle, name, value), $"set thumbnailer {name}");
    }

    private static void Check(int result, string action)
    {
        if (result < 0)
            throw new InvalidOperationException($"Failed to {action}: {LibMpvNative.GetErrorString(result)}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    ~MpvThumbnailer()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_handle != IntPtr.Zero)
                {
                    LibMpvNative.mpv_wakeup(_handle);
                    if (_activeCalls == 0)
                    {
                        LibMpvNative.mpv_terminate_destroy(_handle);
                        _handle = IntPtr.Zero;
                    }
                }
            }

            try
            {
                _lockFile?.Dispose();
            }
            catch (Exception ex) { Log.Debug(ex, "Failed to dispose lock file"); }
        }

        try
        {
            if (Directory.Exists(_thumbnailDirectory))
                Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to delete thumbnail directory during dispose");
            // Temp cleanup is allowed to fail when an image is still being released by UI.
        }
    }

    private sealed class CacheEntry(string path)
    {
        public string Path { get; } = path;
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
    }
}
