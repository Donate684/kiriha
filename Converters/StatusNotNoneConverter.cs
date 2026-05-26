using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Models.Entities;

namespace Kiriha.Converters;

public class StatusNotNoneConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is UserAnimeStatus status)
        {
            return status != UserAnimeStatus.None;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

