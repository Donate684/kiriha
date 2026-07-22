using Kiriha.Core;

namespace Kiriha.Models;

public partial class AppSettings
{
    public class PlayerConfig
    {
        public bool AutoPlay { get; set; } = true;
        public bool SingleWindow { get; set; } = true;
        public bool RememberVolume { get; set; } = true;
        public bool NormalizeAudio { get; set; } = false;
        public double Volume { get; set; } = 100;
        public double PlaybackSpeed { get; set; } = 1.0;
        public bool AutoHideControls { get; set; } = true;
        public bool ShowChapterMarkers { get; set; } = true;
        public PlayerMouseAction LeftClickAction { get; set; } = PlayerMouseAction.TogglePlayPause;
        public PlayerMouseAction RightClickAction { get; set; } = PlayerMouseAction.OpenSettings;
        public PlayerMouseAction MiddleClickAction { get; set; } = PlayerMouseAction.ToggleFullscreen;
        public PlayerWheelAction WheelUpAction { get; set; } = PlayerWheelAction.VolumeUp;
        public PlayerWheelAction WheelDownAction { get; set; } = PlayerWheelAction.VolumeDown;
        public int WheelVolumeStep { get; set; } = 5;
        public int SeekStep { get; set; } = 1;
        public bool ShowPlayPauseButton { get; set; } = true;
        public bool ShowSkipButtons { get; set; } = true;
        public bool ShowMuteButton { get; set; } = true;
        public bool ShowVolumeSlider { get; set; } = true;
        public bool ShowTimeDisplay { get; set; } = true;
        public bool ShowSpeedButton { get; set; } = true;
        public bool ShowSubtitleButton { get; set; } = true;
        public bool ShowSubtitlePositionButton { get; set; } = true;
        public bool ShowAudioButton { get; set; } = true;
        public bool ShowScreenshotButton { get; set; } = true;
        public bool ShowSubtitleStyleButton { get; set; } = true;
        public string PreferredAudioLanguages { get; set; } = "Japanese,jpn,ja";
        public string PreferredSubtitleLanguages { get; set; } = "Russian,rus,ru";
        public bool SubtitleStyleOverrideEnabled { get; set; } = false;
        public string SubtitleStyleHotkey { get; set; } = "U";
        public string SubtitleFont { get; set; } = "Candara Bold";
        public double SubtitleFontSize { get; set; } = 60;
        public string SubtitleColor { get; set; } = "#FFFFFF";
        public string SubtitleBorderColor { get; set; } = "#000000";
        public string SubtitleShadowColor { get; set; } = "#000000";
        public double SubtitleBorderSize { get; set; } = 3.8;
        public double SubtitleShadowOffset { get; set; } = 1.5;
        public string SubtitleAlignY { get; set; } = "bottom";
        public string SubtitleAlignX { get; set; } = "center";
        public int SubtitleMarginY { get; set; } = 35;
        public bool SubtitleScaleByWindow { get; set; } = true;
        public string ScreenshotDirectory { get; set; } = string.Empty;
        public string ScreenshotFormat { get; set; } = "png";
        public string ScreenshotResolutionMode { get; set; } = "video";
        public int ScreenshotPngCompression { get; set; } = 4;
        public int ScreenshotQuality { get; set; } = 95;
        public bool ScreenshotHighBitDepth { get; set; } = false;
        public string ScreenshotWithSubtitlesHotkey { get; set; } = "S";
        public string ScreenshotWithoutSubtitlesHotkey { get; set; } = "Shift+S";
        public string TogglePlayPauseHotkey { get; set; } = "Space";
        public string ToggleFullscreenHotkey { get; set; } = "F";
        public string ExitFullscreenHotkey { get; set; } = "Escape";
        public string ToggleMuteHotkey { get; set; } = "M";
        public string CycleAudioHotkey { get; set; } = "A";
        public string CycleSubtitleHotkey { get; set; } = "V";
        public string PreviousMediaHotkey { get; set; } = "P";
        public string NextMediaHotkey { get; set; } = "N";
        public string SpeedDownHotkey { get; set; } = "OemOpenBrackets";
        public string SpeedUpHotkey { get; set; } = "OemCloseBrackets";
        public string VolumeUpHotkey { get; set; } = "Up";
        public string VolumeDownHotkey { get; set; } = "Down";
        public string SeekBackwardHotkey { get; set; } = "Left";
        public string SeekForwardHotkey { get; set; } = "Right";
        public string ReloadSubtitlesHotkey { get; set; } = "Q";
        public string FrameStepForwardHotkey { get; set; } = "OemPeriod";
        public string FrameStepBackwardHotkey { get; set; } = "OemComma";
        public string MpvScale { get; set; } = "ewa_lanczossharp";
        public string MpvChromaScale { get; set; } = "ewa_lanczossharp";
        public string MpvDitherDepth { get; set; } = "auto";
        public bool MpvCorrectDownscaling { get; set; } = true;
        public bool MpvDeband { get; set; } = true;
        public int MpvDebandIterations { get; set; } = 3;
        public int MpvDebandThreshold { get; set; } = 30;
        public string MpvHwdec { get; set; } = "auto";
        public string MpvVideoOutput { get; set; } = "gpu-next";
        public string MpvGpuApi { get; set; } = "auto";
        public string MpvGpuContext { get; set; } = "auto";
    }
}
