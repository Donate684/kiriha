using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Models.Entities;

namespace Kiriha.Converters;

public class StatusNoneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is UserAnimeStatus status
            ? status == UserAnimeStatus.None
            : true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
