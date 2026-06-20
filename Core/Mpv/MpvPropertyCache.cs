using System;

namespace Kiriha.Core.Mpv;

internal sealed class MpvPropertyCache
{
    private const double TimePositionMinimumChangeSeconds = 0.25;
    private static readonly TimeSpan RuntimeInfoRefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimePositionEventInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private double _lastTimePosition;
    private double _lastPublishedTimePosition;
    private double _lastDuration;
    private bool _lastPause = true;
    private bool _lastSeekable;
    private bool _lastLoaded;
    private DateTime _lastTimePositionEventUtc = DateTime.MinValue;
    private string _runtimeVideoInfo;
    private DateTime _runtimeVideoInfoRefreshedUtc = DateTime.MinValue;
    private bool _runtimeVideoInfoDirty = true;

    public MpvPropertyCache(string initialRuntimeVideoInfo)
    {
        _runtimeVideoInfo = initialRuntimeVideoInfo;
    }

    public double TimePosition
    {
        get
        {
            lock (_gate)
            {
                return _lastTimePosition;
            }
        }
    }

    public double Duration
    {
        get
        {
            lock (_gate)
            {
                return _lastDuration;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _lastPause;
            }
        }
    }

    public string RuntimeVideoInfo
    {
        get
        {
            lock (_gate)
            {
                return _runtimeVideoInfo;
            }
        }
    }

    public PlaybackState PlaybackState
    {
        get
        {
            lock (_gate)
            {
                return CreatePlaybackState();
            }
        }
    }

    public bool HasFreshRuntimeVideoInfo
    {
        get
        {
            lock (_gate)
            {
                return !_runtimeVideoInfoDirty &&
                       DateTime.UtcNow - _runtimeVideoInfoRefreshedUtc < RuntimeInfoRefreshInterval;
            }
        }
    }

    public void StoreRuntimeVideoInfo(string info)
    {
        lock (_gate)
        {
            _runtimeVideoInfo = info;
            _runtimeVideoInfoRefreshedUtc = DateTime.UtcNow;
            _runtimeVideoInfoDirty = false;
        }
    }

    public void InvalidateRuntimeVideoInfo()
    {
        lock (_gate)
        {
            _runtimeVideoInfoDirty = true;
        }
    }

    public bool TryUpdateTimePosition(double timePosition)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var isFirstEvent = _lastTimePositionEventUtc == DateTime.MinValue;
            var changedEnough = Math.Abs(timePosition - _lastPublishedTimePosition) >= TimePositionMinimumChangeSeconds;
            var elapsedEnough = now - _lastTimePositionEventUtc >= TimePositionEventInterval;

            _lastTimePosition = timePosition;

            if (!isFirstEvent && !changedEnough && !elapsedEnough)
                return false;

            _lastPublishedTimePosition = timePosition;
            _lastTimePositionEventUtc = now;
            return true;
        }
    }

    public bool TryUpdateDuration(double duration)
    {
        lock (_gate)
        {
            if (Math.Abs(duration - _lastDuration) <= 0.01)
                return false;

            _lastDuration = duration;
            return true;
        }
    }

    public bool TryUpdatePause(bool isPaused)
    {
        lock (_gate)
        {
            if (isPaused == _lastPause)
                return false;

            _lastPause = isPaused;
            return true;
        }
    }

    public bool TryUpdateSeekable(bool isSeekable)
    {
        lock (_gate)
        {
            if (isSeekable == _lastSeekable)
                return false;

            _lastSeekable = isSeekable;
            return true;
        }
    }

    public bool TryUpdateLoaded(bool isLoaded)
    {
        lock (_gate)
        {
            if (isLoaded == _lastLoaded)
                return false;

            _lastLoaded = isLoaded;
            return true;
        }
    }

    public bool TryUpdatePlaybackEnded()
    {
        lock (_gate)
        {
            var changed = !_lastPause;
            _lastPause = true;
            return changed;
        }
    }

    private PlaybackState CreatePlaybackState() =>
        new(
            _lastTimePosition,
            _lastDuration,
            !_lastPause,
            _lastSeekable,
            _lastLoaded);
}
