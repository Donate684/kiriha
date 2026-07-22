using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Serilog;



namespace Kiriha.Services;

/// <summary>
/// Surfaces user-facing notifications via Windows toast (Action Center).
/// Designed to work even when the app is hidden in the tray. Failures are
/// logged but never thrown — notifications are best-effort UX, not critical path.
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

        // Dedupe — only fire when episode number actually advanced for this anime.
        if (_lastNotifiedEpisode.TryGetValue(anime.Id, out var prev) && prev >= episodeNumber)
            return;
        _lastNotifiedEpisode[anime.Id] = episodeNumber;

        // Build a 2- or 3-line toast: bold original title, optional Russian title, then
        // the episode availability line. "Original" is whichever non-Russian title we have
        // (Title is the user's preferred MAL display title — usually English/romaji).
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
    public virtual void NotifyScrobbleSkipped(Kiriha.Models.AnimeItem anime, int detectedEp)
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

            var xmlString = "<toast><visual><binding template=\"ToastGeneric\">";
            for (int i = 0; i < clamped; i++)
            {
                var text = (lines[i] ?? string.Empty)
                    .Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
                xmlString += $"<text>{text}</text>";
            }
            xmlString += "</binding></visual></toast>";

            var xmlDoc = new Windows.Data.Xml.Dom.XmlDocument();
            xmlDoc.LoadXml(xmlString);

            var toast = new Windows.UI.Notifications.ToastNotification(xmlDoc);
            Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(AumId).Show(toast);
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

}
