using System;

namespace Kiriha.Core.Mpv;

public class MpvTrackManager
{
    private readonly MpvPlayer _player;

    public MpvTrackManager(MpvPlayer player)
    {
        _player = player;
    }

    public void CycleSubtitle()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "cycle", "sid"), "cycle subtitles"));
    }

    public void AdjustSubtitlePosition(double delta)
    {
        _player.Enqueue(handle => MpvPlayer.Check(
            LibMpvNative.mpv_command_string(handle, "add", "sub-pos", MpvPlayer.FormatDouble(delta)),
            "adjust subtitle position"));
    }

    public void CycleAudio()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "cycle", "aid"), "cycle audio"));
    }

    public void ReloadSubtitles()
    {
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "sub-reload"), "reload subtitles"));
    }

    public void SetTrack(string type, string id)
    {
        _player.Enqueue(handle =>
        {
            string prop = type == "sub" ? "sid" : type == "audio" ? "aid" : "vid";
            MpvPlayer.Check(LibMpvNative.mpv_set_property_string(handle, prop, id), $"set {prop}");
        });
    }

    public void SetTrackLanguagePreferences(string audioLanguages, string subtitleLanguages)
    {
        _player.Enqueue(handle =>
        {
            MpvPlayer.SetMpvOption(handle, "alang", audioLanguages, "set preferred audio languages");
            MpvPlayer.SetMpvOption(handle, "slang", subtitleLanguages, "set preferred subtitle languages");
        });
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
        _player.Enqueue(handle =>
        {
            MpvPlayer.Check(LibMpvNative.mpv_set_property_string(handle, "sub-ass-override", enabled ? "force" : "yes"), "set subtitle override");

            if (!enabled)
                return;

            MpvPlayer.SetMpvOption(handle, "sub-font", font, "set subtitle font");
            MpvPlayer.SetMpvOption(handle, "sub-font-size", MpvPlayer.FormatDouble(fontSize), "set subtitle font size");
            MpvPlayer.SetMpvOption(handle, "sub-color", color, "set subtitle color");
            MpvPlayer.SetMpvOption(handle, "sub-border-color", borderColor, "set subtitle border color");
            MpvPlayer.SetMpvOption(handle, "sub-shadow-color", shadowColor, "set subtitle shadow color");
            MpvPlayer.SetMpvOption(handle, "sub-border-size", MpvPlayer.FormatDouble(borderSize), "set subtitle border size");
            MpvPlayer.SetMpvOption(handle, "sub-shadow-offset", MpvPlayer.FormatDouble(shadowOffset), "set subtitle shadow offset");
            MpvPlayer.SetMpvOption(handle, "sub-align-y", alignY, "set subtitle vertical alignment");
            MpvPlayer.SetMpvOption(handle, "sub-align-x", alignX, "set subtitle horizontal alignment");
            MpvPlayer.SetMpvOption(handle, "sub-margin-y", Math.Max(0, marginY).ToString(System.Globalization.CultureInfo.InvariantCulture), "set subtitle margin");
            MpvPlayer.SetMpvOption(handle, "sub-scale-by-window", scaleByWindow ? "yes" : "no", "set subtitle scaling");
        });
    }

    public void AddSubtitle(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _player.Enqueue(handle => MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "sub-add", path), "add subtitle"));
    }
}
