using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kiriha.Utils.UI;

/// <summary>
/// Intercepts mouse-wheel scrolling on a ScrollViewer and eases the current
/// offset toward the accumulated target offset.
///
/// Wheel ticks are accumulated into _targetY instead of restarting a tween on
/// every tick. That keeps fast scrolling smooth on high-refresh displays.
/// Animation is driven by TopLevel.RequestAnimationFrame, avoiding DispatcherTimer
/// jitter and staying aligned with the compositor frame.
/// </summary>
public sealed class SmoothScrollBehavior
{
    private readonly ScrollViewer _sv;
    private double _targetY;
    private bool _animating;
    private bool _frameRequested;
    private TimeSpan _lastFrameTime = TimeSpan.MinValue;

    /// <summary>Whether smooth scrolling is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How many pixels to scroll per mouse-wheel tick.</summary>
    public double WheelMultiplier { get; set; } = 110;

    /// <summary>
    /// Exponential smoothing time constant. Smaller values feel sharper/faster.
    /// 110 ms feels close to Edge: about 70% of the distance is covered in one tau.
    /// </summary>
    public TimeSpan SmoothingTime { get; set; } = TimeSpan.FromMilliseconds(110);

    public SmoothScrollBehavior(ScrollViewer sv)
    {
        _sv = sv ?? throw new ArgumentNullException(nameof(sv));
        _targetY = sv.Offset.Y;

        // Tunnel with handledEventsToo so we intercept before ScrollViewer applies
        // its own immediate wheel step.
        _sv.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public static SmoothScrollBehavior Attach(ScrollViewer sv) => new(sv);

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!Enabled) return;

        // Positive Delta.Y means wheel up, so the vertical offset decreases.
        var delta = -e.Delta.Y * WheelMultiplier;
        if (Math.Abs(delta) < 0.0001) return;

        var maxY = Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height);

        // If animation is idle, external code may have moved the offset
        // through ScrollIntoView, navigation, or reset. Re-anchor the target.
        if (!_animating) _targetY = _sv.Offset.Y;

        var newTarget = Math.Clamp(_targetY + delta, 0, maxY);

        // Already at the boundary and still pulling outward: let parent/chained
        // scroll handling see the event.
        if (Math.Abs(newTarget - _sv.Offset.Y) < 0.5 && !_animating)
            return;

        _targetY = newTarget;
        _animating = true;

        RequestFrame();
        e.Handled = true;
    }

    private void RequestFrame()
    {
        if (_frameRequested) return;
        var top = TopLevel.GetTopLevel(_sv);
        if (top == null) return;
        _frameRequested = true;
        top.RequestAnimationFrame(OnFrame);
    }

    /// <summary>Reset animation state, for example after content layout changes.</summary>
    public void Cancel()
    {
        _animating = false;
        _targetY = _sv.Offset.Y;
        _lastFrameTime = TimeSpan.MinValue;
    }

    private void OnFrame(TimeSpan now)
    {
        _frameRequested = false;
        if (!_animating)
        {
            _lastFrameTime = TimeSpan.MinValue;
            return;
        }

        // Content may have changed size while animating; clamp the target to the
        // current scrollable range to avoid easing toward an invalid point.
        var maxY = Math.Max(0, _sv.Extent.Height - _sv.Viewport.Height);
        if (_targetY > maxY) _targetY = maxY;
        if (_targetY < 0) _targetY = 0;

        // First frame in a series: we only know the timestamp now, so wait for
        // the next frame before integrating.
        if (_lastFrameTime == TimeSpan.MinValue)
        {
            _lastFrameTime = now;
            RequestFrame();
            return;
        }

        var dt = (now - _lastFrameTime).TotalMilliseconds;
        _lastFrameTime = now;

        // Cap long pauses such as window minimize or GC pause; otherwise alpha
        // jumps to 1 and the scroll visibly snaps to the target.
        if (dt > 100) dt = 100;
        if (dt <= 0) { RequestFrame(); return; }

        var tau = Math.Max(1, SmoothingTime.TotalMilliseconds);
        var alpha = 1.0 - Math.Exp(-dt / tau);
        var current = _sv.Offset.Y;
        var next = current + (_targetY - current) * alpha;

        // Close enough: snap exactly and stop requesting frames.
        if (Math.Abs(_targetY - next) < 0.4)
        {
            _sv.Offset = new Vector(_sv.Offset.X, _targetY);
            _animating = false;
            _lastFrameTime = TimeSpan.MinValue;
            return;
        }

        _sv.Offset = new Vector(_sv.Offset.X, next);
        RequestFrame();
    }
}
