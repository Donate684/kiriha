using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;
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
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    public void Initialize(MpvPlayer player)
    {
        _playback.Attach(player);
        _playback.FileLoaded += OnPlayerFileLoaded;
        _playback.PlaybackEnded += OnPlayerPlaybackEnded;
        _playback.PlaybackStateChanged += OnPlayerPlaybackStateChanged;
        _playback.TracksChanged += OnPlayerTracksChanged;
        _playback.SetVolume(Volume);
        _playback.SetSpeed(PlaybackSpeed);
        _playback.SetAudioNormalization(NormalizeAudio);
        ApplyTrackLanguagePreferences();
        ApplyVideoProcessingOptions();
        ApplyScreenshotOptions();
        ApplySubtitleStyleOverride();
        _timelinePreview.Initialize();
        _timelinePreview.WarmUp(VideoUrl);
        
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _statePublisher.Connect();
    }

    public void LoadSubtitle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        _playback.AddSubtitle(path);
    }

    public void LoadVideo(string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
            return;

        if (!string.Equals(VideoUrl, videoUrl, StringComparison.Ordinal))
            VideoUrl = videoUrl;

        ApplyTimelineSnapshot(_timeline.Reset());
        IsPlaying = PlayerAutoPlay;
        IsLoading = true;
        HasPlaybackError = false;
        PlaybackErrorMessage = string.Empty;
        PlaybackStatusMessage = "Загрузка видео...";

        if (!_playback.HasPlayer)
            return;

        if (!PlayerAutoPlay)
            _playback.Pause();

        _playback.Load(videoUrl);

        if (PlayerAutoPlay)
            _playback.Play();

        _statePublisher.Publish();
    }

    private double _lastPublishedPosition = -1;
    private bool _lastPublishedIsPlaying;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (!_playback.HasPlayer) return;

        if (Duration <= 0)
            RefreshDurationFromPlayer();

        if (IsPlaying || _lastPublishedIsPlaying != IsPlaying || Math.Abs(_lastPublishedPosition - CurrentTime) > 0.5)
        {
            _statePublisher.Publish();
            _lastPublishedPosition = CurrentTime;
            _lastPublishedIsPlaying = IsPlaying;
        }

        if (_mpvRuntimeDiagnosticsVisible)
            RefreshMpvRuntimeInfo();
    }

    public void SetMpvRuntimeDiagnosticsVisible(bool visible)
    {
        if (_mpvRuntimeDiagnosticsVisible == visible)
            return;

        _mpvRuntimeDiagnosticsVisible = visible;

        if (visible)
            RefreshMpvRuntimeInfo();
    }

    private void ShowOsd(string message, string detail = "")
    {
        Overlay.ShowOsd(message, detail);
    }

    private CancellationTokenSource? _updateTracksCts;

    public void UpdateTracks()
    {
        var oldCts = _updateTracksCts;
        _updateTracksCts = new CancellationTokenSource();

        try { oldCts?.Cancel(); } catch (ObjectDisposedException) { }
        oldCts?.Dispose();

        _ = UpdateTracksAsync(_updateTracksCts.Token);
    }

    private void RefreshDurationFromPlayer()
    {
        ApplyPlaybackState(_playback.GetPlaybackState());
    }

    private async Task UpdateTracksAsync(CancellationToken token)
    {
        var result = await _playback.GetTracksAndChaptersAsync();
        if (result == null || token.IsCancellationRequested)
            return;

        var (tracks, chapters) = result.Value;
        
        var audioTracks = tracks.Where(t => t.Type == "audio").ToList();
        var subTracks = tracks.Where(t => t.Type == "sub").ToList();

        if (token.IsCancellationRequested) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;

            AudioTracks.Clear();
            SubtitleTracks.Clear();

            // Add 'None' option for subtitles
            SubtitleTracks.Add(new TrackInfo { Id = "no", Type = "sub", Title = "Отключить" });

            foreach (var t in audioTracks) AudioTracks.Add(t);
            foreach (var t in subTracks) SubtitleTracks.Add(t);

            Chapters.Clear();
            foreach (var ch in chapters)
                Chapters.Add(ch);
        });
    }

    [RelayCommand]
    private void SelectTrack(TrackInfo track)
    {
        if (_playback.HasPlayer && track != null)
        {
            _playback.SetTrack(track.Type, track.Id);
            ShowOsd(track.Type == "sub" ? "Субтитры" : "Аудио", track.DisplayName);
        }
    }

    public void BeginScrub()
    {
        _timeline.BeginScrub();
    }

    public void EndScrub()
    {
        if (_playback.HasPlayer)
        {
            var snapshot = _timeline.EndScrub(CurrentTime);
            _playback.Seek(snapshot.CurrentTime);
            ApplyTimelineSnapshot(snapshot);
        }
    }

    // Called when CurrentTime is changed via UI while scrubbing
    partial void OnCurrentTimeChanged(double value)
    {
        if (_timeline.IsScrubbing)
            ApplyTimelineSnapshot(_timeline.UpdateScrubTime(value));
    }

    partial void OnVolumeChanged(double value)
    {
        if (IsMuted)
        {
            // Unmute automatically if user drags the slider
            _isMuted = false;
            OnPropertyChanged(nameof(IsMuted));
        }

        if (_playback.HasPlayer)
        {
            _playback.SetVolume(value);
        }

        if (!_isApplyingSettings && RememberPlayerVolume && _settingsService != null)
        {
            _settingsService.Update(settings => settings.Player.Volume = Math.Clamp(value, 0, 100), SettingsSection.Player);
        }

        if (!_isApplyingSettings)
            ShowOsd("Громкость", $"{Math.Clamp(value, 0, 100):0}%");
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        var speed = Math.Clamp(value, 0.1, 4.0);
        _playback.SetSpeed(speed);

        if (!_isApplyingSettings && _settingsService != null)
        {
            _settingsService.Update(settings => settings.Player.PlaybackSpeed = speed, SettingsSection.Player);
        }

        if (!_isApplyingSettings)
            ShowOsd("Скорость", $"{speed:0.##}x");
    }

    partial void OnNormalizeAudioChanged(bool value)
    {
        _playback.SetAudioNormalization(value);

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.NormalizeAudio = value, SettingsSection.Player);
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_playback.HasPlayer) return;
        
        if (value)
        {
            _previousVolume = Volume;
            _playback.SetVolume(0);
            ShowOsd("Звук", "выключен");
        }
        else
        {
            _playback.SetVolume(Volume);
            ShowOsd("Звук", $"{Math.Clamp(Volume, 0, 100):0}%");
        }
    }
}
