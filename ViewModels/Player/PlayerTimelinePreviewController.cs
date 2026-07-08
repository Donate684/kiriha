using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Kiriha.Core.Mpv;

namespace Kiriha.ViewModels.Player;

public sealed class PlayerTimelinePreviewController : IDisposable
{
    private readonly PlayerOverlayViewModel _overlay;
    private MpvThumbnailer? _thumbnailer;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _warmUpCts;
    private int _requestId;
    private int _previewBucket = -1;
    private string? _previewImagePath;

    public PlayerTimelinePreviewController(PlayerOverlayViewModel overlay)
    {
        _overlay = overlay;
    }

    public void Initialize()
    {
        _thumbnailer ??= CreateThumbnailer();
    }

    public async void Show(string videoUrl, double duration, double timeSeconds, double left)
    {
        if (duration <= 0 || string.IsNullOrWhiteSpace(videoUrl))
        {
            Hide();
            return;
        }

        _overlay.ShowTimelinePreview(timeSeconds, left);

        var thumbnailer = _thumbnailer;
        if (thumbnailer == null)
            return;

        var bucket = MpvThumbnailer.GetCacheBucket(timeSeconds);
        if (bucket == _previewBucket && _overlay.TimelinePreviewImage != null)
            return;

        _previewBucket = bucket;
        var requestId = ++_requestId;
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        try
        {
            var path = await thumbnailer.GetThumbnailAsync(videoUrl, timeSeconds, token);
            if (token.IsCancellationRequested || requestId != _requestId || string.IsNullOrWhiteSpace(path))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestId != _requestId || !File.Exists(path))
                    return;

                if (string.Equals(_previewImagePath, path, StringComparison.Ordinal))
                    return;

                _overlay.SetTimelinePreviewImage(new Bitmap(path));
                _previewImagePath = path;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to show timeline preview");
        }
    }

    public void Hide()
    {
        _requestId++;
        _previewBucket = -1;
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
        _overlay.HideTimelinePreview();
    }

    public void WarmUp(string videoUrl)
    {
        var thumbnailer = _thumbnailer;
        if (thumbnailer == null || string.IsNullOrWhiteSpace(videoUrl))
            return;

        _warmUpCts?.Cancel();
        _warmUpCts?.Dispose();
        _warmUpCts = new CancellationTokenSource();
        var token = _warmUpCts.Token;

        _ = Task.Run(() =>
        {
            if (token.IsCancellationRequested || !File.Exists(videoUrl))
                return Task.CompletedTask;

            return thumbnailer.WarmUpAsync(videoUrl, token)
                .ContinueWith(task =>
                {
                    if (task.Exception != null)
                        Serilog.Log.Debug(task.Exception, "Failed to warm up timeline thumbnailer");
                }, TaskContinuationOptions.OnlyOnFaulted);
        });
    }

    public void Dispose()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
        _warmUpCts?.Cancel();
        _warmUpCts?.Dispose();
        _warmUpCts = null;
        _previewImagePath = null;
        _thumbnailer?.Dispose();
        _thumbnailer = null;
        _overlay.ClearTimelinePreview();
    }

    private static MpvThumbnailer? CreateThumbnailer()
    {
        try
        {
            return new MpvThumbnailer();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Timeline thumbnailer is unavailable");
            return null;
        }
    }
}
