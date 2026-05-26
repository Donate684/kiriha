using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels;

public partial class PlayerViewModel
{
    [RelayCommand]
    private void TogglePlayPause()
    {
        if (_player == null) return;
        
        if (IsPlaying)
            _player.Pause();
        else
            _player.Play();
            
        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void CycleSubtitle()
    {
        _player?.CycleSubtitle();
    }

    [RelayCommand]
    private void CycleAudio()
    {
        _player?.CycleAudio();
    }

    [RelayCommand]
    public void ToggleSubtitleStyleOverride()
    {
        SubtitleStyleOverrideEnabled = !SubtitleStyleOverrideEnabled;
    }

    [RelayCommand]
    public void MoveSubtitleUp()
    {
        _player?.AdjustSubtitlePosition(-1);
        ShowOsd("Субтитры", "выше");
    }

    [RelayCommand]
    public void MoveSubtitleDown()
    {
        _player?.AdjustSubtitlePosition(1);
        ShowOsd("Субтитры", "ниже");
    }

    [RelayCommand]
    private void TakeScreenshot()
    {
        TakeScreenshot(includeSubtitles: false);
    }

    public void TakeScreenshot(bool includeSubtitles)
    {
        _player?.TakeScreenshot(includeSubtitles, ScreenshotResolution?.Value ?? "video");
        ShowOsd("Скриншот", includeSubtitles ? "с субтитрами" : "без субтитров");
    }

    [RelayCommand]
    private void SetSpeed(object parameter)
    {
        if (parameter != null && double.TryParse(parameter.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double speed))
        {
            PlaybackSpeed = speed;
        }
    }


    [RelayCommand]
    private void Skip(object parameter)
    {
        if (_player != null && parameter != null && int.TryParse(parameter.ToString(), out int seconds))
        {
            double newTime = Math.Max(0, Math.Min(Duration, CurrentTime + seconds));
            _player.Seek(newTime);
            CurrentTime = newTime;
            CurrentTimeString = TimeSpan.FromSeconds(newTime).ToString(@"hh\:mm\:ss");
            _lastSeekTime = DateTime.Now;
        }
    }

    public void SeekTo(double time)
    {
        if (_player != null)
        {
            double newTime = Math.Max(0, Math.Min(Duration, time));
            _player.Seek(newTime);
            CurrentTime = newTime;
            CurrentTimeString = TimeSpan.FromSeconds(newTime).ToString(@"hh\:mm\:ss");
            _lastSeekTime = DateTime.Now;
        }
    }

    public void SeekRelative(double seconds)
    {
        SeekTo(CurrentTime + seconds);
        ShowOsd(seconds >= 0 ? "Вперёд" : "Назад", $"{Math.Abs(seconds):0.#} сек");
    }

    public void AdjustVolume(double delta)
    {
        Volume = Math.Clamp(Volume + delta, 0, 100);
    }

    public async void ShowTimelinePreview(double timeSeconds, double left)
    {
        if (Duration <= 0 || string.IsNullOrWhiteSpace(VideoUrl) || !File.Exists(VideoUrl))
        {
            HideTimelinePreview();
            return;
        }

        TimelinePreviewLeft = Math.Max(0, left);
        TimelinePreviewTime = FormatTime(timeSeconds);
        IsTimelinePreviewVisible = true;

        var thumbnailer = _thumbnailer;
        if (thumbnailer == null)
            return;

        var bucket = MpvThumbnailer.GetCacheBucket(timeSeconds);
        if (bucket == _timelinePreviewBucket && TimelinePreviewImage != null)
            return;

        _timelinePreviewBucket = bucket;
        var requestId = ++_thumbnailRequestId;
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        var token = _thumbnailCts.Token;

        try
        {
            var path = await thumbnailer.GetThumbnailAsync(VideoUrl, timeSeconds, token);
            if (token.IsCancellationRequested || requestId != _thumbnailRequestId || string.IsNullOrWhiteSpace(path))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestId != _thumbnailRequestId || !File.Exists(path))
                    return;

                if (string.Equals(_timelinePreviewImagePath, path, StringComparison.Ordinal))
                    return;

                TimelinePreviewImage?.Dispose();
                TimelinePreviewImage = new Bitmap(path);
                _timelinePreviewImagePath = path;
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

    public void HideTimelinePreview()
    {
        _thumbnailRequestId++;
        _timelinePreviewBucket = -1;
        _thumbnailCts?.Cancel();
        IsTimelinePreviewVisible = false;
    }

    public void Dispose()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
        _thumbnailWarmUpCts?.Cancel();
        _thumbnailWarmUpCts?.Dispose();
        _thumbnailWarmUpCts = null;
        TimelinePreviewImage?.Dispose();
        TimelinePreviewImage = null;
        _timelinePreviewImagePath = null;
        _thumbnailer?.Dispose();
        _thumbnailer = null;
        _timer?.Stop();
        _timer = null;
        _osdTimer?.Stop();
        _osdTimer = null;
        if (_player != null)
        {
            _player.FileLoaded -= OnPlayerFileLoaded;
            _player.PlaybackEnded -= OnPlayerPlaybackEnded;
            _player.TimePositionChanged -= OnPlayerTimePositionChanged;
            _player.DurationChanged -= OnPlayerDurationChanged;
            _player.PauseChanged -= OnPlayerPauseChanged;
        }
        _player = null;
        _stateClient.PublishClosed();
        _stateClient.Dispose();
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

    private void WarmUpThumbnailer()
    {
        var thumbnailer = _thumbnailer;
        if (thumbnailer == null || string.IsNullOrWhiteSpace(VideoUrl) || !File.Exists(VideoUrl))
            return;

        _thumbnailWarmUpCts?.Cancel();
        _thumbnailWarmUpCts?.Dispose();
        _thumbnailWarmUpCts = new CancellationTokenSource();
        _ = thumbnailer.WarmUpAsync(VideoUrl, _thumbnailWarmUpCts.Token)
            .ContinueWith(task =>
            {
                if (task.Exception != null)
                    Serilog.Log.Debug(task.Exception, "Failed to warm up timeline thumbnailer");
            }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private Kiriha.Models.Api.InternalPlayerState CreatePlayerState()
    {
        var titleToUse = !string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu;
        if (string.IsNullOrEmpty(titleToUse))
            titleToUse = AnimeTitle;

        return new Kiriha.Models.Api.InternalPlayerState
        {
            AnimeId = _animeId,
            OriginalTitle = System.IO.Path.GetFileNameWithoutExtension(VideoUrl),
            AnimeTitle = titleToUse,
            Episode = RawEpisodeText,
            Position = CurrentTime,
            Duration = Duration,
            IsPlaying = IsPlaying
        };
    }

    private void OnPlayerFileLoaded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateTracks();
            RefreshMpvRuntimeInfo();
            WarmUpThumbnailer();
        });
    }

    private void OnPlayerPlaybackEnded(object? sender, MpvPlaybackEndedEventArgs e)
    {
        if (!e.StopsPlayback)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            _stateClient.Publish(CreatePlayerState());
        });
    }

    private void OnPlayerTimePositionChanged(double time)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isScrubbing || (DateTime.Now - _lastSeekTime).TotalMilliseconds <= 500)
                return;

            if (Math.Abs(time - CurrentTime) > 0.5)
            {
                CurrentTime = time;
                CurrentTimeString = FormatTime(time);
            }
        });
    }

    private void OnPlayerDurationChanged(double duration)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (duration <= 0 || Math.Abs(duration - Duration) <= 0.01)
                return;

            Duration = duration;
            DurationString = FormatTime(duration);
        });
    }

    private void OnPlayerPauseChanged(bool isPaused)
    {
        Dispatcher.UIThread.Post(() => IsPlaying = !isPaused);
    }

    private void RefreshMpvRuntimeInfo()
    {
        if (_player == null)
            return;

        MpvRuntimeInfo = _player.GetRuntimeVideoInfo();
    }

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
}
