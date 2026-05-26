using System.Collections.Generic;
using Serilog;

namespace Kiriha.Services.Tracking.Anisthesia;

/// <summary>
/// Translates raw <see cref="AudioState"/> samples (one per polling tick of
/// <c>AnisthesiaService</c>) into a debounced "is the player playing?" signal,
/// and emits Serilog lines on every Play&#x2194;Pause transition so we can
/// audit the scrobble timeline post-hoc.
///
/// Design choices:
/// <list type="bullet">
/// <item>A PID is considered playing until we see <see cref="InactiveTicksToPause"/>
/// consecutive <see cref="AudioState.Inactive"/> samples — guards against
/// short silent scenes (intro fades, dialogue gaps).</item>
/// <item>A PID that has never been <see cref="AudioState.Active"/> stays
/// "playing" — the video might genuinely have no audio track, and we don't
/// want to permanently mark such sessions as paused.</item>
/// <item>Once flipped to paused, a single <see cref="AudioState.Active"/>
/// sample resumes immediately (humans expect responsive resume).</item>
/// </list>
/// </summary>
internal sealed class PauseDetector
{
    /// <summary>
    /// Number of consecutive <see cref="AudioState.Inactive"/> samples
    /// required before flipping to paused. With the 5 s polling interval
    /// in <c>AnisthesiaService</c> this maps to ~10 s of silence.
    /// </summary>
    private const int InactiveTicksToPause = 2;

    private sealed class Tracker
    {
        public bool EverActive;
        public int InactiveStreak;
        public bool ReportedPaused;
    }

    private readonly Dictionary<uint, Tracker> _trackers = new();

    /// <summary>
    /// Drop bookkeeping for PIDs that no longer correspond to a detected
    /// player. Prevents the dictionary from growing unboundedly across a
    /// long session of opening/closing players.
    /// </summary>
    public void Forget(uint pid)
    {
        _trackers.Remove(pid);
    }

    /// <summary>
    /// Update the per-PID state with a fresh sample and return whether the
    /// player should currently be considered as playing. <paramref name="contextLabel"/>
    /// is used purely for log readability (e.g. "Title ep N").
    /// </summary>
    public bool Update(uint pid, AudioState sample, string contextLabel)
    {
        if (pid == 0) return true; // unknown PID — assume playing, don't track

        if (!_trackers.TryGetValue(pid, out var t))
        {
            t = new Tracker();
            _trackers[pid] = t;
        }

        switch (sample)
        {
            case AudioState.Active:
                t.EverActive = true;
                t.InactiveStreak = 0;
                if (t.ReportedPaused)
                {
                    Log.Information("Scrobbler: resumed [{Context}] (PID {Pid})", contextLabel, pid);
                    t.ReportedPaused = false;
                }
                return true;

            case AudioState.Inactive:
                // Until we've seen audio at least once, treat Inactive as
                // "no audio track" rather than a pause signal.
                if (!t.EverActive) return true;

                t.InactiveStreak++;
                if (t.InactiveStreak >= InactiveTicksToPause)
                {
                    if (!t.ReportedPaused)
                    {
                        Log.Information("Scrobbler: paused [{Context}] (PID {Pid}, audio inactive {Ticks} ticks)",
                            contextLabel, pid, t.InactiveStreak);
                        t.ReportedPaused = true;
                    }
                    return false;
                }
                // Below the debounce threshold — still considered playing.
                return !t.ReportedPaused ? true : false;

            case AudioState.Expired:
                // Session ended — fall back to "playing" so the scrobbler
                // doesn't get stuck on paused if the player exited cleanly;
                // process-disappearance is handled separately upstream.
                t.InactiveStreak = 0;
                if (t.ReportedPaused)
                {
                    Log.Information("Scrobbler: audio session expired [{Context}] (PID {Pid})", contextLabel, pid);
                    t.ReportedPaused = false;
                }
                return true;

            case AudioState.Unknown:
            default:
                // No session yet (or COM failed once). Don't penalize the
                // player — keep the previous reported state. A real pause
                // requires explicit Inactive samples.
                return !t.ReportedPaused;
        }
    }
}
