using System;

namespace Kiriha.ViewModels;

public sealed class PlayerTimelineService
{
    private static readonly TimeSpan SeekEchoSuppression = TimeSpan.FromMilliseconds(500);

    private DateTime _lastSeekTime = DateTime.MinValue;

    public double CurrentTime { get; private set; }
    public double Duration { get; private set; }
    public string CurrentTimeString { get; private set; } = "00:00";
    public string DurationString { get; private set; } = "--:--";
    public bool IsScrubbing { get; private set; }

    public PlayerTimelineSnapshot Snapshot =>
        new(CurrentTime, Duration, CurrentTimeString, DurationString);

    public PlayerTimelineSnapshot Reset()
    {
        CurrentTime = 0;
        Duration = 0;
        CurrentTimeString = "00:00";
        DurationString = "--:--";
        IsScrubbing = false;
        _lastSeekTime = DateTime.MinValue;
        return Snapshot;
    }

    public bool TrySetDuration(double duration, out PlayerTimelineSnapshot snapshot)
    {
        snapshot = Snapshot;
        if (duration <= 0 || Math.Abs(duration - Duration) <= 0.01)
            return false;

        Duration = duration;
        DurationString = FormatTime(duration);
        snapshot = Snapshot;
        return true;
    }

    public PlayerTimelineSnapshot SeekTo(double time)
    {
        CurrentTime = ClampToDuration(time);
        CurrentTimeString = FormatTime(CurrentTime);
        _lastSeekTime = DateTime.Now;
        return Snapshot;
    }

    public void BeginScrub()
    {
        IsScrubbing = true;
    }

    public PlayerTimelineSnapshot EndScrub(double currentTime)
    {
        IsScrubbing = false;
        return SeekTo(currentTime);
    }

    public PlayerTimelineSnapshot UpdateScrubTime(double currentTime)
    {
        CurrentTime = ClampToDuration(currentTime);
        CurrentTimeString = FormatTime(CurrentTime);
        return Snapshot;
    }

    public bool TryApplyPlayerTime(double time, out PlayerTimelineSnapshot snapshot)
    {
        snapshot = Snapshot;
        if (IsScrubbing || (DateTime.Now - _lastSeekTime) <= SeekEchoSuppression)
            return false;

        if (Math.Abs(time - CurrentTime) <= 0.5)
            return false;

        CurrentTime = Math.Max(0, time);
        CurrentTimeString = FormatTime(CurrentTime);
        snapshot = Snapshot;
        return true;
    }

    public static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"hh\:mm\:ss") : ts.ToString(@"mm\:ss");
    }

    private double ClampToDuration(double time)
    {
        return Duration > 0
            ? Math.Max(0, Math.Min(Duration, time))
            : Math.Max(0, time);
    }
}

public readonly record struct PlayerTimelineSnapshot(
    double CurrentTime,
    double Duration,
    string CurrentTimeString,
    string DurationString);
