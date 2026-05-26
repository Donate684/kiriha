using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kiriha.Models.Entities;

namespace Kiriha.Converters;

public class StatusToColorConverter : IValueConverter
{
    private static readonly ISolidColorBrush WatchingBrush = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly ISolidColorBrush CompletedBrush = new SolidColorBrush(Color.Parse("#1565C0"));
    private static readonly ISolidColorBrush OnHoldBrush = new SolidColorBrush(Color.Parse("#F9A825"));
    private static readonly ISolidColorBrush DroppedBrush = new SolidColorBrush(Color.Parse("#C62828"));
    private static readonly ISolidColorBrush PlanToWatchBrush = new SolidColorBrush(Color.Parse("#455A64"));
    private static readonly ISolidColorBrush DefaultBrush = new SolidColorBrush(Colors.Gray);
    private static readonly ISolidColorBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is UserAnimeStatus status)
        {
            return status switch
            {
                UserAnimeStatus.Watching => WatchingBrush,
                UserAnimeStatus.Completed => CompletedBrush,
                UserAnimeStatus.OnHold => OnHoldBrush,
                UserAnimeStatus.Dropped => DroppedBrush,
                UserAnimeStatus.PlanToWatch => PlanToWatchBrush,
                _ => DefaultBrush
            };
        }
        return TransparentBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
