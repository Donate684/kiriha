using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kiriha.Core.Mpv;

namespace Kiriha.ViewModels;

public sealed class PlayerPlaybackController
{
    private MpvPlayer? _player;

    public event EventHandler? FileLoaded;
    public event EventHandler<MpvPlaybackEndedEventArgs>? PlaybackEnded;
    public event Action<PlaybackState>? PlaybackStateChanged;
    public event Action? TracksChanged;

    public bool HasPlayer => _player != null;

    public void Attach(MpvPlayer player)
    {
        Detach();

        _player = player;
        _player.FileLoaded += OnFileLoaded;
        _player.PlaybackEnded += OnPlaybackEnded;
        _player.PlaybackStateChanged += OnPlaybackStateChanged;
        _player.TracksChanged += OnTracksChanged;
    }

    public void Detach()
    {
        if (_player == null)
            return;

        _player.FileLoaded -= OnFileLoaded;
        _player.PlaybackEnded -= OnPlaybackEnded;
        _player.PlaybackStateChanged -= OnPlaybackStateChanged;
        _player.TracksChanged -= OnTracksChanged;
        _player = null;
    }

    public void Load(string videoUrl) => _player?.Load(videoUrl);
    public void Play() => _player?.Play();
    public void Pause() => _player?.Pause();
    public void Seek(double timeInSeconds) => _player?.Seek(timeInSeconds);
    public void SetVolume(double volume) => _player?.SetVolume(volume);
    public void SetSpeed(double speed) => _player?.SetSpeed(speed);
    public void SetAudioNormalization(bool enabled) => _player?.SetAudioNormalization(enabled);
    public void CycleSubtitle() => _player?.CycleSubtitle();
    public void CycleAudio() => _player?.CycleAudio();
    public void AdjustSubtitlePosition(double delta) => _player?.AdjustSubtitlePosition(delta);
    public void TakeScreenshot(bool includeSubtitles, string resolutionMode) => _player?.TakeScreenshot(includeSubtitles, resolutionMode);
    public void SetTrack(string type, string id) => _player?.SetTrack(type, id);
    public void SetOptionString(string name, string value) => _player?.SetOptionString(name, value);
    public double GetDuration() => _player?.GetDuration() ?? 0;
    public PlaybackState GetPlaybackState() => _player?.GetPlaybackState() ?? new PlaybackState(0, 0, false, false, false);
    public string GetRuntimeVideoInfo() => _player?.GetRuntimeVideoInfo() ?? "hwdec: -, interop: -, vo: -, context: -, decoder: -";
    public void SetScreenshotOptions(
        string directory,
        string format,
        int pngCompression,
        int quality,
        bool highBitDepth)
    {
        _player?.SetScreenshotOptions(directory, format, pngCompression, quality, highBitDepth);
    }

    public void SetTrackLanguagePreferences(string audioLanguages, string subtitleLanguages)
    {
        _player?.SetTrackLanguagePreferences(audioLanguages, subtitleLanguages);
    }

    public void SetVideoProcessingOptions(
        string scale,
        string chromaScale,
        string ditherDepth,
        bool correctDownscaling,
        bool deband,
        int debandIterations,
        int debandThreshold)
    {
        _player?.SetVideoProcessingOptions(
            scale,
            chromaScale,
            ditherDepth,
            correctDownscaling,
            deband,
            debandIterations,
            debandThreshold);
    }

    public void SetSubtitleStyleOverride(
        bool enabled,
        string font,
        double fontSize,
        string color,
        string borderColor,
        string shadowColor,
        double borderSize,
        double shadowOffset,
        string alignY,
        string alignX,
        int marginY,
        bool scaleByWindow)
    {
        _player?.SetSubtitleStyleOverride(
            enabled,
            font,
            fontSize,
            color,
            borderColor,
            shadowColor,
            borderSize,
            shadowOffset,
            alignY,
            alignX,
            marginY,
            scaleByWindow);
    }

    public async Task<(List<TrackInfo> Tracks, List<ChapterInfo> Chapters)?> GetTracksAndChaptersAsync()
    {
        var player = _player;
        if (player == null)
            return null;

        try
        {
            var result = await Task.Run(() => (Tracks: player.GetTracks(), Chapters: player.GetChapters()));
            return ReferenceEquals(_player, player) ? result : null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to refresh mpv tracks");
            return null;
        }
    }

    private void OnFileLoaded(object? sender, EventArgs e) => FileLoaded?.Invoke(sender, e);
    private void OnPlaybackEnded(object? sender, MpvPlaybackEndedEventArgs e) => PlaybackEnded?.Invoke(sender, e);
    private void OnPlaybackStateChanged(PlaybackState state) => PlaybackStateChanged?.Invoke(state);
    private void OnTracksChanged() => TracksChanged?.Invoke();
}
