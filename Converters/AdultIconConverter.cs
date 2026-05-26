using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.ViewModels;

namespace Kiriha.Converters;

public class AdultIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is AdultFilterMode mode
            ? mode switch
            {
                AdultFilterMode.Hide => "HandBackLeftOffOutline",
                AdultFilterMode.Include => "HandBackLeftOutline",
                AdultFilterMode.Only => "AlertOctagon",
                _ => "Help"
            }
            : "Help";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
}
