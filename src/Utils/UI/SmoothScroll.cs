using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Avalonia;
using Avalonia.Controls;

namespace Kiriha.Utils.UI;

/// <summary>
/// AttachedProperty-обёртка над <see cref="SmoothScrollBehavior"/>:
/// <c>u:SmoothScroll.IsEnabled="True"</c> на любом ScrollViewer.
/// </summary>
public static class SmoothScroll
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "IsEnabled", typeof(SmoothScroll));

    private static readonly AttachedProperty<SmoothScrollBehavior?> BehaviorProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, SmoothScrollBehavior?>(
            "Behavior", typeof(SmoothScroll));

    public static bool GetIsEnabled(ScrollViewer sv) => sv.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer sv, bool value) => sv.SetValue(IsEnabledProperty, value);

    static SmoothScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((sv, e) =>
        {
            var enabled = e.NewValue is true;
            var existing = sv.GetValue(BehaviorProperty);

            if (enabled && existing == null)
            {
                var b = SmoothScrollBehavior.Attach(sv);
                sv.SetValue(BehaviorProperty, b);
            }
            else if (existing != null)
            {
                existing.Enabled = enabled;
            }
        });
    }
}
