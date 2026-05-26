using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kiriha.Models;

namespace Kiriha.Converters;

public class StarColorConverter : IValueConverter
{
    private static readonly IBrush GoldBrush = Brushes.Gold;
    private static readonly IBrush EliteBrush = Brushes.DeepSkyBlue;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string paramStr = parameter as string ?? "";
        string context = "MyList";
        if (paramStr.Contains(":"))
        {
            var parts = paramStr.Split(':');
            if (parts.Length > 1) context = parts[1];
        }
        else if (!string.IsNullOrEmpty(paramStr) && !int.TryParse(paramStr, out _))
        {
             context = paramStr;
        }

        if (value == null) return GoldBrush;

        string? scoreStr = null;
        if (value is RatingOption option)
        {
            scoreStr = option.Value;
        }
        else if (value is string s)
        {
            scoreStr = s;
        }

        if (RatingHelper.IsElite(scoreStr, context))
        {
            return EliteBrush;
        }

        return GoldBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
