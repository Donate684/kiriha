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
        if (!_playback.HasPlayer) return;
        
        if (IsPlaying)
            _playback.Pause();
        else
            _playback.Play();
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    [RelayCommand]
    private void CycleSubtitle()
    {
        _playback.CycleSubtitle();
    }

    [RelayCommand]
    private void CycleAudio()
    {
        _playback.CycleAudio();
    }

    [RelayCommand]
    public void ToggleSubtitleStyleOverride()
    {
        SubtitleStyleOverrideEnabled = !SubtitleStyleOverrideEnabled;
    }

    [RelayCommand]
    public void MoveSubtitleUp()
    {
        _playback.AdjustSubtitlePosition(-1);
        ShowOsd("Субтитры", "выше");
    }

    [RelayCommand]
    public void MoveSubtitleDown()
    {
        _playback.AdjustSubtitlePosition(1);
        ShowOsd("Субтитры", "ниже");
    }

    [RelayCommand]
    private void TakeScreenshot()
    {
        TakeScreenshot(includeSubtitles: false);
    }

    public void TakeScreenshot(bool includeSubtitles)
    {
        _playback.TakeScreenshot(includeSubtitles, ScreenshotResolution?.Value ?? "video");
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
        if (_playback.HasPlayer && parameter != null && int.TryParse(parameter.ToString(), out int seconds))
        {
            var snapshot = _timeline.SeekTo(CurrentTime + seconds);
            _playback.Seek(snapshot.CurrentTime);
            ApplyTimelineSnapshot(snapshot);
        }
    }

    [RelayCommand]
    public void OpenPreviousMedia()
    {
        if (!string.IsNullOrEmpty(_previousMediaPath))
            LoadVideo(_previousMediaPath);
    }

    [RelayCommand]
    public void OpenNextMedia()
    {
        if (!string.IsNullOrEmpty(_nextMediaPath))
            LoadVideo(_nextMediaPath);
    }

    [RelayCommand]
    public void ReloadMedia()
    {
        if (!string.IsNullOrWhiteSpace(VideoUrl))
            LoadVideo(VideoUrl);
    }

    public void SeekTo(double time)
    {
        if (_playback.HasPlayer)
        {
            var snapshot = _timeline.SeekTo(time);
            _playback.Seek(snapshot.CurrentTime);
            ApplyTimelineSnapshot(snapshot);
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

    public void AdjustPlaybackSpeed(double delta)
    {
        PlaybackSpeed = Math.Clamp(PlaybackSpeed + delta, 0.25, 2.0);
    }

    public void ShowTimelinePreview(double timeSeconds, double left)
    {
        _timelinePreview.Show(VideoUrl, Duration, timeSeconds, left);
    }

    public void HideTimelinePreview()
    {
        _timelinePreview.Hide();
    }

    public void Dispose()
    {
        _updateTracksCts?.Cancel();
        _updateTracksCts?.Dispose();
        _timelinePreview.Dispose();
        _timer?.Stop();
        _timer = null;
        Overlay.Dispose();
        _playback.FileLoaded -= OnPlayerFileLoaded;
        _playback.PlaybackEnded -= OnPlayerPlaybackEnded;
        _playback.PlaybackStateChanged -= OnPlayerPlaybackStateChanged;
        _playback.TracksChanged -= OnPlayerTracksChanged;
        _playback.Detach();
        _statePublisher.PublishClosed();
        _statePublisher.Dispose();
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
            IsLoading = false;
            HasPlaybackError = false;
            PlaybackErrorMessage = string.Empty;
            PlaybackStatusMessage = "Готово";
            UpdateNavigationAvailability();
            RefreshDurationFromPlayer();
            UpdateTracks();
            RefreshMpvRuntimeInfo();
            _timelinePreview.WarmUp(VideoUrl);
            _statePublisher.Publish();
        });
    }

    private void OnPlayerTracksChanged()
    {
        Dispatcher.UIThread.Post(() => UpdateTracks());
    }

    private void OnPlayerPlaybackEnded(object? sender, MpvPlaybackEndedEventArgs e)
    {
        if (!e.StopsPlayback)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsLoading = false;
            IsPlaying = false;
            if (e.HasError)
            {
                HasPlaybackError = true;
                PlaybackErrorMessage = string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? "Не удалось открыть или воспроизвести файл."
                    : e.ErrorMessage;
                PlaybackStatusMessage = "Ошибка воспроизведения";
            }

            _statePublisher.Publish();
        });
    }

    private void OnPlayerPlaybackStateChanged(PlaybackState state)
    {
        Dispatcher.UIThread.Post(() => ApplyPlaybackState(state));
    }

    private void ApplyPlaybackState(PlaybackState state)
    {
        PlayerTimelineSnapshot? snapshot = null;

        if (state.Duration > 0 && _timeline.TrySetDuration(state.Duration, out var durationSnapshot))
            snapshot = durationSnapshot;

        if (_timeline.TryApplyPlayerTime(state.Position, out var positionSnapshot))
            snapshot = positionSnapshot;

        if (snapshot.HasValue)
            ApplyTimelineSnapshot(snapshot.Value);

        IsPlaying = state.IsPlaying;
    }

    private string? _previousMediaPath;
    private string? _nextMediaPath;

    private CancellationTokenSource? _updateNavigationCts;

    private void UpdateNavigationAvailability()
    {
        _previousMediaPath = null;
        _nextMediaPath = null;
        CanOpenPreviousMedia = false;
        CanOpenNextMedia = false;

        var videoUrl = VideoUrl;
        if (string.IsNullOrWhiteSpace(videoUrl))
            return;

        _updateNavigationCts?.Cancel();
        _updateNavigationCts?.Dispose();
        _updateNavigationCts = new CancellationTokenSource();

        _ = Task.Run(() => UpdateNavigationAvailabilityAsync(videoUrl, _updateNavigationCts.Token));
    }

    private void UpdateNavigationAvailabilityAsync(string videoUrl, CancellationToken token)
    {
        if (!File.Exists(videoUrl))
            return;

        var directory = Path.GetDirectoryName(videoUrl);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        try
        {
            var files = Directory.EnumerateFiles(directory)
                .Where(IsSupportedMediaPath)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (token.IsCancellationRequested) return;

            var currentIndex = files.FindIndex(x => string.Equals(x, videoUrl, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0)
            {
                string? previousMediaPath = null;
                string? nextMediaPath = null;
                bool canOpenPreviousMedia = false;
                bool canOpenNextMedia = false;

                if (currentIndex > 0)
                {
                    previousMediaPath = files[currentIndex - 1];
                    canOpenPreviousMedia = true;
                }

                if (currentIndex < files.Count - 1)
                {
                    nextMediaPath = files[currentIndex + 1];
                    canOpenNextMedia = true;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (token.IsCancellationRequested) return;

                    _previousMediaPath = previousMediaPath;
                    _nextMediaPath = nextMediaPath;
                    CanOpenPreviousMedia = canOpenPreviousMedia;
                    CanOpenNextMedia = canOpenNextMedia;
                });
            }
        }
        catch
        {
            // Ignore directory access errors
        }
    }

    private static bool IsSupportedMediaPath(string path)
    {
        return MediaExtensions.Contains(Path.GetExtension(path));
    }

    private void RefreshMpvRuntimeInfo()
    {
        if (!_playback.HasPlayer)
            return;

        MpvRuntimeInfo = _playback.GetRuntimeVideoInfo();
    }

    private void ApplyTimelineSnapshot(PlayerTimelineSnapshot snapshot)
    {
        CurrentTime = snapshot.CurrentTime;
        Duration = snapshot.Duration;
        CurrentTimeString = snapshot.CurrentTimeString;
        DurationString = snapshot.DurationString;
    }
}
