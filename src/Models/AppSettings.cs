using System.Collections.Generic;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models.Api;

namespace Kiriha.Models;

public class AppSettings
{
    public UiConfig UI { get; set; } = new();
    public SystemConfig System { get; set; } = new();
    public PlayerConfig Player { get; set; } = new();
    public TorrentConfig Torrents { get; set; } = new();
    public ApiConfig Api { get; set; } = new();

    /// <summary>
    /// User-defined share buttons. Surfaced next to MAL/Shiki in the anime
    /// details window and the now-playing card. See <see cref="CustomShareLink"/>.
    /// </summary>
    public List<CustomShareLink> CustomLinks { get; set; } = new();

    public class UiConfig
    {
        public ThemeType Theme { get; set; } = ThemeType.System;

        public string LanguageCode { get; set; } = Constants.Languages.En;
        public bool UseRussianTitles { get; set; } = false;
        public bool UseRussianDescriptions { get; set; } = false;
        public bool EnableMica { get; set; } = true;
        public bool ShowAiringInfo { get; set; } = true;
        public string SeasonalSortBy { get; set; } = Constants.Sorting.Popularity;
        public string ListSortBy { get; set; } = Constants.Sorting.Title;
        public List<string> SeasonalStatusFilters { get; set; } = new() { "All" };
        public bool ShowNsfw { get; set; } = false;
        public bool ListShowNsfw { get; set; } = false;
        public bool IsPaneOpen { get; set; } = true;
        
        /// <summary>
        /// App-level UI scale factor (1.0 = 100%).
        /// Applied via LayoutTransformControl on MainWindow root.
        /// Range: 0.25–3.0.
        /// </summary>
        public double UiScale { get; set; } = 1.0;

        /// <summary>
        /// Client-only: ids of seasonal anime the user marked as "not interested".
        /// Hidden from the seasonal grid by default; toggle <c>SeasonalShowHidden</c>
        /// to bring them back for un-hiding.
        /// </summary>
        public List<int> HiddenSeasonalIds { get; set; } = new();
        public bool SeasonalShowHidden { get; set; } = false;

        /// <summary>
        /// Last known main-window geometry. Persisted across launches so that
        /// the user-resized window comes back at the same size/position no
        /// matter how the app was started (shortcut, --minimized, tray, etc.).
        /// </summary>
        public WindowPlacement Window { get; set; } = new();
    }

    public class WindowPlacement
    {
        /// <summary>Outer width in DIPs. <c>0</c> means "first run - use default".</summary>
        public double Width { get; set; }
        /// <summary>Outer height in DIPs. <c>0</c> means "first run - use default".</summary>
        public double Height { get; set; }
        /// <summary>Top-left in screen pixels. <c>null</c> = center on primary screen.</summary>
        public int? X { get; set; }
        public int? Y { get; set; }
        /// <summary>Whether the window was maximized at the moment of last save.</summary>
        public bool Maximized { get; set; }
    }

    public class SystemConfig
    {
        public bool CloseToTray { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public bool AutoLaunch { get; set; } = true;
        public bool LaunchMinimized { get; set; } = true;
        public bool AutoCheckUpdates { get; set; } = true;
        public bool AutoDownloadUpdates { get; set; } = true;
        public bool KeepPlayerProcessAlive { get; set; } = false;
        public bool NotifyNewEpisodes { get; set; } = true;
        public bool NotifyAppUpdate { get; set; } = true;
        /// <summary>Delay (in minutes) between detecting a new episode and firing the toast.
        /// 0 = fire immediately. Useful to batch notifications or wait for subs to land.</summary>
        public int NewEpisodeNotificationDelayMinutes { get; set; } = 0;
        public List<string> CompletedSetupSteps { get; set; } = new();
        public ScrobblerConfig Scrobbler { get; set; } = new();
        public bool EnableDiscordRPC { get; set; } = true;
        public bool EnableBackgroundMetadataFetch { get; set; } = true;
    }

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

    public class ScrobblerConfig
    {
        public bool Enabled { get; set; } = true;
        public int DelaySeconds { get; set; } = 300;
        public List<string> AllowedProcesses { get; set; } = new();
        public bool AutoMatchConfidence { get; set; } = true;

        /// <summary>
        /// When true and the detected episode number skips ahead beyond the next
        /// expected episode (e.g. progress=5 but watching ep 7), the scrobbler
        /// fires a toast notification instead of attempting an update that the
        /// API may reject or that would mark intermediate episodes incorrectly.
        /// </summary>
        public bool NotifyOnSkippedEpisode { get; set; } = true;
    }

    public class TorrentConfig
    {
        public bool OnlyCrunchyroll { get; set; } = false;
        public bool FilterNetflix { get; set; } = false;
        public bool FilterAmazon { get; set; } = false;
        public bool FilterHidive { get; set; } = false;
        public bool FilterVaryg { get; set; } = false;
        public bool FilterEraiRaws { get; set; } = false;
        public bool FilterToonsHub { get; set; } = false;
        public bool FilterHevc { get; set; } = false;
        public bool Filter1080p { get; set; } = false;
        public System.Collections.Generic.List<int> HiddenAnimeIds { get; set; } = new();

        /// <summary>When true, filter toggles are remembered per anime title.</summary>
        public bool FiltersPerTitle { get; set; } = false;
        public System.Collections.Generic.Dictionary<int, TorrentFilterSet> PerTitleFilters { get; set; } = new();
    }

    public class TorrentFilterSet
    {
        public bool OnlyCrunchyroll { get; set; }
        public bool FilterNetflix { get; set; }
        public bool FilterAmazon { get; set; }
        public bool FilterHidive { get; set; }
        public bool FilterVaryg { get; set; }
        public bool FilterEraiRaws { get; set; }
        public bool FilterToonsHub { get; set; }
        public bool FilterHevc { get; set; }
        public bool Filter1080p { get; set; }
    }

    public class ApiConfig
    {
        public MalTokens? Mal { get; set; }
        public ShikiTokens? Shiki { get; set; }
        public ShikiMirror ShikiMirror { get; set; } = ShikiMirror.One;
    }
}

public enum ShikiMirror
{
    One = 0,   // shikimori.one (the canonical site)
    Net = 1    // shikimori.net (a.k.a. shiki.rip - independent OAuth realm)
}

public enum PlayerMouseAction
{
    None = 0,
    TogglePlayPause = 1,
    ToggleFullscreen = 2,
    ShowControls = 3,
    OpenSettings = 4,
    SeekBackward10 = 5,
    SeekForward10 = 6,
    CycleAudio = 7,
    CycleSubtitle = 8
}

public enum PlayerWheelAction
{
    None = 0,
    VolumeUp = 1,
    VolumeDown = 2,
    SeekForward = 3,
    SeekBackward = 4,
    SpeedUp = 5,
    SpeedDown = 6
}
