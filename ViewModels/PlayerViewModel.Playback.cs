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
        _playback.TimePositionChanged -= OnPlayerTimePositionChanged;
        _playback.DurationChanged -= OnPlayerDurationChanged;
        _playback.PauseChanged -= OnPlayerPauseChanged;
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
            IsPlaying = false;
            _statePublisher.Publish();
        });
    }

    private void OnPlayerTimePositionChanged(double time)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_timeline.TryApplyPlayerTime(time, out var snapshot))
                ApplyTimelineSnapshot(snapshot);
        });
    }

    private void OnPlayerDurationChanged(double duration)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_timeline.TrySetDuration(duration, out var snapshot))
                ApplyTimelineSnapshot(snapshot);
        });
    }

    private void OnPlayerPauseChanged(bool isPaused)
    {
        Dispatcher.UIThread.Post(() => IsPlaying = !isPaused);
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
