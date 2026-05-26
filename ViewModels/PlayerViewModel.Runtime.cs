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
    public void Initialize(MpvPlayer player)
    {
        _player = player;
        _player.FileLoaded += OnPlayerFileLoaded;
        _player.PlaybackEnded += OnPlayerPlaybackEnded;
        _player.TimePositionChanged += OnPlayerTimePositionChanged;
        _player.DurationChanged += OnPlayerDurationChanged;
        _player.PauseChanged += OnPlayerPauseChanged;
        _player.SetVolume(Volume);
        _player.SetSpeed(PlaybackSpeed);
        _player.SetAudioNormalization(NormalizeAudio);
        ApplyTrackLanguagePreferences();
        ApplyVideoProcessingOptions();
        ApplyScreenshotOptions();
        ApplySubtitleStyleOverride();
        _thumbnailer ??= CreateThumbnailer();
        WarmUpThumbnailer();
        
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        _osdTimer.Tick += OnOsdTimerTick;

        _ = _stateClient.ConnectAsync();
    }

    public void LoadVideo(string videoUrl)
    {
        if (string.IsNullOrWhiteSpace(videoUrl))
            return;

        if (!string.Equals(VideoUrl, videoUrl, StringComparison.Ordinal))
            VideoUrl = videoUrl;

        CurrentTime = 0;
        Duration = 100;
        CurrentTimeString = "00:00";
        DurationString = "00:00";
        IsPlaying = PlayerAutoPlay;

        if (_player == null)
            return;

        if (!PlayerAutoPlay)
            _player.Pause();

        _player.Load(videoUrl);

        if (PlayerAutoPlay)
            _player.Play();

        _stateClient.Publish(CreatePlayerState());
    }

    private DateTime _lastSeekTime = DateTime.MinValue;

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_player == null) return;

        _stateClient.Publish(CreatePlayerState());
        RefreshMpvRuntimeInfo();
    }

    private void ShowOsd(string message, string detail = "")
    {
        OsdMessage = message;
        OsdDetail = detail;
        IsOsdVisible = true;

        if (_osdTimer == null)
            return;

        _osdTimer.Stop();
        _osdTimer.Start();
    }

    private void OnOsdTimerTick(object? sender, EventArgs e)
    {
        _osdTimer?.Stop();
        IsOsdVisible = false;
    }

    public void UpdateTracks()
    {
        _ = UpdateTracksAsync();
    }

    private async Task UpdateTracksAsync()
    {
        var player = _player;
        if (player == null)
            return;

        List<TrackInfo> tracks;
        List<ChapterInfo> chapters;
        try
        {
            (tracks, chapters) = await Task.Run(() => (player.GetTracks(), player.GetChapters()));
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to refresh mpv tracks");
            return;
        }

        if (_player != player)
            return;

        AudioTracks.Clear();
        SubtitleTracks.Clear();

        // Add 'None' option for subtitles
        SubtitleTracks.Add(new TrackInfo { Id = "no", Type = "sub", Title = "Отключить" });

        foreach (var track in tracks)
        {
            if (track.Type == "audio") AudioTracks.Add(track);
            else if (track.Type == "sub") SubtitleTracks.Add(track);
        }

        Chapters.Clear();
        foreach (var ch in chapters)
            Chapters.Add(ch);
    }

    [RelayCommand]
    private async Task SelectTrack(TrackInfo track)
    {
        if (_player != null && track != null)
        {
            _player.SetTrack(track.Type, track.Id);
            await Task.Delay(120);
            UpdateTracks();
            ShowOsd(track.Type == "sub" ? "Субтитры" : "Аудио", track.DisplayName);
        }
    }

    public void BeginScrub()
    {
        _isScrubbing = true;
    }

    public void EndScrub()
    {
        if (_player != null)
        {
            _player.Seek(CurrentTime);
            _lastSeekTime = DateTime.Now;
        }
        _isScrubbing = false;
    }

    // Called when CurrentTime is changed via UI while scrubbing
    partial void OnCurrentTimeChanged(double value)
    {
        if (_isScrubbing)
        {
            CurrentTimeString = TimeSpan.FromSeconds(value).ToString(@"hh\:mm\:ss");
        }
    }

    partial void OnVolumeChanged(double value)
    {
        if (IsMuted)
        {
            // Unmute automatically if user drags the slider
            _isMuted = false;
            OnPropertyChanged(nameof(IsMuted));
        }

        if (_player != null)
        {
            _player.SetVolume(value);
        }

        if (!_isApplyingSettings && RememberPlayerVolume && _settingsService != null)
        {
            _settingsService.Update(settings => settings.Player.Volume = Math.Clamp(value, 0, 100));
        }

        if (!_isApplyingSettings)
            ShowOsd("Громкость", $"{Math.Clamp(value, 0, 100):0}%");
    }

    partial void OnPlaybackSpeedChanged(double value)
    {
        var speed = Math.Clamp(value, 0.1, 4.0);
        _player?.SetSpeed(speed);

        if (!_isApplyingSettings && _settingsService != null)
        {
            _settingsService.Update(settings => settings.Player.PlaybackSpeed = speed);
        }

        if (!_isApplyingSettings)
            ShowOsd("Скорость", $"{speed:0.##}x");
    }

    partial void OnNormalizeAudioChanged(bool value)
    {
        _player?.SetAudioNormalization(value);

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.NormalizeAudio = value);
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_player == null) return;
        
        if (value)
        {
            _previousVolume = Volume;
            _player.SetVolume(0);
            ShowOsd("Звук", "выключен");
        }
        else
        {
            _player.SetVolume(Volume);
            ShowOsd("Звук", $"{Math.Clamp(Volume, 0, 100):0}%");
        }
    }
}
