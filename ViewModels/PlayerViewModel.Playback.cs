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
        if (TryGetAdjacentMediaPath(previous: true, out var path))
            LoadVideo(path);
    }

    [RelayCommand]
    public void OpenNextMedia()
    {
        if (TryGetAdjacentMediaPath(previous: false, out var path))
            LoadVideo(path);
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
        _timelinePreview.Dispose();
        _timer?.Stop();
        _timer = null;
        Overlay.Dispose();
        _playback.FileLoaded -= OnPlayerFileLoaded;
        _playback.PlaybackEnded -= OnPlayerPlaybackEnded;
        _playback.PlaybackStateChanged -= OnPlayerPlaybackStateChanged;
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

    private bool TryGetAdjacentMediaPath(bool previous, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(VideoUrl) || !File.Exists(VideoUrl))
            return false;

        var directory = Path.GetDirectoryName(VideoUrl);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var files = Directory.EnumerateFiles(directory)
            .Where(IsSupportedMediaPath)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var currentIndex = files.FindIndex(x => string.Equals(x, VideoUrl, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return false;

        var adjacentIndex = previous ? currentIndex - 1 : currentIndex + 1;
        if (adjacentIndex < 0 || adjacentIndex >= files.Count)
            return false;

        path = files[adjacentIndex];
        return true;
    }

    private void UpdateNavigationAvailability()
    {
        CanOpenPreviousMedia = TryGetAdjacentMediaPath(previous: true, out _);
        CanOpenNextMedia = TryGetAdjacentMediaPath(previous: false, out _);
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
