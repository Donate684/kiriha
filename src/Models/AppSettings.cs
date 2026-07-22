using System.Collections.Generic;
using Kiriha.Core;
using Kiriha.Models.Api;

namespace Kiriha.Models;

public partial class AppSettings
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
        public bool EnableLogging { get; set; } = false;
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

    public class ApiConfig
    {
        public MalTokens? Mal { get; set; }
        public ShikiTokens? Shiki { get; set; }
        public ShikiMirror ShikiMirror { get; set; } = ShikiMirror.One;
    }
}
