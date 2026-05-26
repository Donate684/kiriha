using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Serilog;

#if WINDOWS
using Microsoft.Toolkit.Uwp.Notifications;
#endif

namespace Kiriha.Services;

/// <summary>
/// Surfaces user-facing notifications via Windows toast (Action Center).
/// Designed to work even when the app is hidden in the tray. Failures are
/// logged but never thrown Ã¢â‚¬â€ notifications are best-effort UX, not critical path.
/// </summary>
public class NotificationService
{
    private readonly SettingsService _settingsService;
    private readonly IBackgroundTaskSupervisor _backgroundTasks;

    // De-dup: don't fire the same "new episode N for anime X" toast twice in a row.
    // Keyed by anime id, value = the last EpisodesAired count we notified for.
    private readonly ConcurrentDictionary<int, int> _lastNotifiedEpisode = new();

    // De-dup: don't fire the same "new app version" toast twice in a row.
    private string? _lastNotifiedVersion;

    private const string AumId = "Kiriha";

    // Lazily extracted PNG path used as the toast's appLogoOverride. We keep it under
    // BasePath/toast-icon.png so the path is stable across launches and the Windows
    // toast broker (different process) can read it as a regular file.
    private static string? _toastIconPath;

    public NotificationService(SettingsService settingsService, IBackgroundTaskSupervisor backgroundTasks)
    {
        _settingsService = settingsService;
        _backgroundTasks = backgroundTasks;
        TrySetAppUserModelId();
    }

    public void NotifyNewEpisode(AnimeItem anime, int episodeNumber)
    {
        if (anime == null) return;
        if (!_settingsService.Current.System.NotifyNewEpisodes) return;
        if (episodeNumber <= 0) return;

        // Dedupe Ã¢â‚¬â€ only fire when episode number actually advanced for this anime.
        if (_lastNotifiedEpisode.TryGetValue(anime.Id, out var prev) && prev >= episodeNumber)
            return;
        _lastNotifiedEpisode[anime.Id] = episodeNumber;

        // Build a 2- or 3-line toast: bold original title, optional Russian title, then
        // the episode availability line. "Original" is whichever non-Russian title we have
        // (Title is the user's preferred MAL display title Ã¢â‚¬â€ usually English/romaji).
        var orig = !string.IsNullOrEmpty(anime.Title) ? anime.Title : anime.RussianTitle ?? "Anime";
        var ru = anime.RussianTitle;
        var episodeLine = UIUtils.GetLoc("notifications.new_episode.body", episodeNumber);

        // Order: episode line on top (bold by template), then English title, then Russian.
        var lines = new System.Collections.Generic.List<string> { episodeLine, orig };
        if (!string.IsNullOrEmpty(ru) && !string.Equals(ru, orig, StringComparison.Ordinal))
            lines.Add(ru!);

        // Snapshot the delay at the moment of detection. If the user changes it later
        // mid-wait we keep the original behaviour for already-queued notifications.
        var delayMinutes = Math.Max(0, _settingsService.Current.System.NewEpisodeNotificationDelayMinutes);

        if (delayMinutes == 0)
        {
            Log.Information("NotificationService: New episode toast for {Title} ep {Ep}", orig, episodeNumber);
            Show(lines);
            return;
        }

        Log.Information("NotificationService: Scheduling new episode toast for {Title} ep {Ep} in {Min} min",
            orig, episodeNumber, delayMinutes);

        _backgroundTasks.Run("NotificationService.DelayedToast", async ct =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(delayMinutes), ct);
                Show(lines);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "NotificationService: delayed toast failed");
            }
        });
    }

    /// <summary>
    /// Fired by the scrobbler when the playing episode is ahead of the user's
    /// progress by more than one episode and <c>NotifyOnSkippedEpisode</c> is on.
    /// Surfaces a toast so the user knows progress was NOT updated.
    /// </summary>
    public void NotifyScrobbleSkipped(Kiriha.Models.AnimeItem anime, int detectedEp)
    {
        if (anime == null) return;

        var orig = !string.IsNullOrEmpty(anime.Title) ? anime.Title : anime.RussianTitle ?? "Anime";
        var ru = anime.RussianTitle;

        var title = UIUtils.GetLoc("scrobbler.skip_notify.title");
        var body = UIUtils.GetLoc("scrobbler.skip_notify.body", detectedEp, anime.Progress + 1);

        var lines = new System.Collections.Generic.List<string> { title, body, orig };
        if (!string.IsNullOrEmpty(ru) && !string.Equals(ru, orig, StringComparison.Ordinal))
            lines.Add(ru!);

        Log.Information("NotificationService: Scrobble-skip toast for {Title} ep {Ep} (expected {Expected})",
            orig, detectedEp, anime.Progress + 1);
        Show(lines);
    }

    public void NotifyAppUpdate(string newVersion)
    {
        if (string.IsNullOrEmpty(newVersion)) return;
        if (!_settingsService.Current.System.NotifyAppUpdate) return;
        if (_lastNotifiedVersion == newVersion) return;
        _lastNotifiedVersion = newVersion;

        var title = UIUtils.GetLoc("notifications.app_update.title");
        var body = UIUtils.GetLoc("notifications.app_update.body", newVersion);

        Log.Information("NotificationService: Update toast for version {Version}", newVersion);
        Show(new System.Collections.Generic.List<string> { title, body });
    }

    /// <summary>
    /// Renders a toast with up to 3 text lines. The first line is bolded by the
    /// system template; remaining lines render as regular body text. We pick the
    /// template that matches the line count + icon availability so the system
    /// schema validator is happy without us hand-rolling ToastGeneric XML
    /// (which silently fails on minor format slips).
    /// </summary>
    private void Show(System.Collections.Generic.IReadOnlyList<string> lines)
    {
        if (lines == null || lines.Count == 0) return;
        try
        {
#if WINDOWS
            var clamped = lines.Count > 3 ? 3 : lines.Count;

            var builder = new ToastContentBuilder();
            for (int i = 0; i < clamped; i++)
            {
                builder.AddText(lines[i] ?? string.Empty);
            }

            // By using ToastContentBuilder from the toolkit, we get automatic handling of the AUMID
            // and Start Menu shortcut creation if running outside of Velopack (e.g. from IDE).
            // We intentionally do NOT use AddAppLogoOverride, so Windows will use our app's executable icon
            // in the tiny top header, giving us the exact look of MAA.
            builder.Show();
#else
            Log.Debug("NotificationService: Toast not shown (non-Windows build): {Lines}", string.Join(" | ", lines));
#endif
        }
        catch (Exception ex)
        {
            // Common reasons: app not installed via Velopack (no Start Menu shortcut with AUMID),
            // running portable/dev build, or Action Center disabled. Don't surface to the user.
            Log.Warning(ex, "NotificationService: Failed to show toast '{First}'", lines.Count > 0 ? lines[0] : "<empty>");
        }
    }

    private static void TrySetAppUserModelId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                SetCurrentProcessExplicitAppUserModelID(AumId);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "NotificationService: SetCurrentProcessExplicitAppUserModelID failed (non-fatal)");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    /// <summary>
    /// Copies the embedded <c>kiriha.png</c> to a stable disk path the Windows toast
    /// broker can read. Returns the path on success, or null if extraction failed
    /// (in which case we just send a text-only toast).
    /// </summary>
    private static string? EnsureToastIcon()
    {
        if (!string.IsNullOrEmpty(_toastIconPath) && File.Exists(_toastIconPath))
            return _toastIconPath;

        try
        {
            // Sit next to settings.json so it inherits the BasePath/portable layout.
            var dir = Path.GetDirectoryName(PathHelper.GetSettingsPath());
            if (string.IsNullOrEmpty(dir))
                dir = Path.GetTempPath();
            Directory.CreateDirectory(dir);

            // Different filename than the previous "toast-icon.png" so we re-extract
            // even if an older PNG is cached on disk from a prior build.
            var dest = Path.Combine(dir, "toast-icon-v2.png");
            if (!File.Exists(dest) || new FileInfo(dest).Length == 0)
            {
                var uri = new Uri("avares://Kiriha/Assets/kiriha_notif.png");
                using var src = AssetLoader.Open(uri);
                using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read);
                src.CopyTo(fs);
            }

            _toastIconPath = dest;
            return dest;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "NotificationService: failed to extract toast icon");
            return null;
        }
    }
}
