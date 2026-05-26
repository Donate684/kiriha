using Avalonia;
using Avalonia.Controls;

namespace Kiriha.Utils;

/// <summary>
/// AttachedProperty-ÃÂ¾ÃÂ±Ã‘â€˜Ã‘â‚¬Ã‘â€šÃÂºÃÂ° ÃÂ½ÃÂ°ÃÂ´ <see cref="SmoothScrollBehavior"/>:
/// <c>u:SmoothScroll.IsEnabled="True"</c> ÃÂ½ÃÂ° ÃÂ»Ã‘Å½ÃÂ±ÃÂ¾ÃÂ¼ ScrollViewer.
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
