using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kiriha.ViewModels;

namespace Kiriha.Converters;

public class AdultColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AdultFilterMode mode
            ? mode switch
            {
                AdultFilterMode.Hide => Brushes.Gray,
                AdultFilterMode.Include => Brushes.HotPink,
                AdultFilterMode.Only => Brushes.DarkRed,
                _ => Brushes.Gray
            }
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
