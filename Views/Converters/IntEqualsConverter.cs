using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kiriha.Views.Converters;

public class IntEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return false;
        int v;
        try { v = System.Convert.ToInt32(value); }
        catch { return false; }

        int p;
        try { p = System.Convert.ToInt32(parameter); }
        catch { return false; }

        return v == p;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
