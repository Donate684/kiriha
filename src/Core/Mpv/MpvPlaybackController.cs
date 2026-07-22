using System;

namespace Kiriha.Core.Mpv;

public class MpvPlaybackController
{
    private const string AudioNormalizationFilter = "loudnorm=I=-16:TP=-1.5:LRA=11";
    private const string SeekCommandKey = "seek";
    private const string VolumeCommandKey = "volume";
    private const string SpeedCommandKey = "speed";

    private readonly MpvPlayer _player;

    public MpvPlaybackController(MpvPlayer player)
    {
        _player = player;
    }

    public void Load(string url)
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "loadfile", url), "load file"));
    }

    public void Play()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_set_property_string(handle, "pause", "no"), "play"));
    }

    public void Pause()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_set_property_string(handle, "pause", "yes"), "pause"));
    }

    public void Seek(double timeInSeconds)
    {
        _player.Enqueue(handle => MpvPlayer.Check(
            LibMpvNative.mpv_command_string(handle, "seek", timeInSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute"),
            "seek"), SeekCommandKey);
    }

    public void SetVolume(double volume)
    {
        _player.Enqueue(handle =>
        {
            double vol = Math.Max(0, Math.Min(100, volume));
            MpvPlayer.Check(LibMpvNative.mpv_set_property(handle, "volume", LibMpvNative.MPV_FORMAT_DOUBLE, ref vol), "set volume");
        }, VolumeCommandKey);
    }

    public void SetSpeed(double speed)
    {
        _player.Enqueue(handle =>
        {
            double spd = Math.Max(0.1, Math.Min(4.0, speed));
            MpvPlayer.Check(LibMpvNative.mpv_set_property(handle, "speed", LibMpvNative.MPV_FORMAT_DOUBLE, ref spd), "set speed");
        }, SpeedCommandKey);
    }

    public void SetAudioNormalization(bool enabled)
    {
        _player.Enqueue(handle => MpvPlayer.Check(
            LibMpvNative.mpv_set_property_string(handle, "af", enabled ? AudioNormalizationFilter : string.Empty),
            enabled ? "enable audio normalization" : "disable audio normalization"));
    }

    public void FrameStep()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "frame-step"), "frame step"));
    }

    public void FrameBackStep()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "frame-back-step"), "frame back step"));
    }
}
