using Kiriha.ViewModels.Player;

namespace Kiriha.Tests;

public sealed class PlayerTimelineServiceTests
{
    [Fact]
    public void Reset_ReturnsInitialTimelineState()
    {
        var timeline = new PlayerTimelineService();

        var snapshot = timeline.Reset();

        Assert.Equal(0, snapshot.CurrentTime);
        Assert.Equal(0, snapshot.Duration);
        Assert.Equal("00:00", snapshot.CurrentTimeString);
        Assert.Equal("--:--", snapshot.DurationString);
        Assert.False(timeline.IsScrubbing);
    }

    [Fact]
    public void TrySetDuration_FormatsDurationAndIgnoresTinyChanges()
    {
        var timeline = new PlayerTimelineService();

        Assert.True(timeline.TrySetDuration(125, out var snapshot));
        Assert.Equal(125, snapshot.Duration);
        Assert.Equal("02:05", snapshot.DurationString);

        Assert.False(timeline.TrySetDuration(125.005, out _));
    }

    [Fact]
    public void SeekTo_ClampsToDurationAndSuppressesImmediatePlayerEcho()
    {
        var timeline = new PlayerTimelineService();
        timeline.TrySetDuration(60, out _);

        var snapshot = timeline.SeekTo(90);

        Assert.Equal(60, snapshot.CurrentTime);
        Assert.Equal("01:00", snapshot.CurrentTimeString);
        Assert.False(timeline.TryApplyPlayerTime(12, out _));
    }

    [Fact]
    public void Scrubbing_UpdatesDisplayedTimeWithoutAcceptingPlayerTime()
    {
        var timeline = new PlayerTimelineService();
        timeline.TrySetDuration(100, out _);

        timeline.BeginScrub();
        var scrubSnapshot = timeline.UpdateScrubTime(42);

        Assert.True(timeline.IsScrubbing);
        Assert.Equal(42, scrubSnapshot.CurrentTime);
        Assert.Equal("00:42", scrubSnapshot.CurrentTimeString);
        Assert.False(timeline.TryApplyPlayerTime(50, out _));

        var endSnapshot = timeline.EndScrub(84);

        Assert.False(timeline.IsScrubbing);
        Assert.Equal(84, endSnapshot.CurrentTime);
    }
}
