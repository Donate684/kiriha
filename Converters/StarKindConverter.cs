using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Models;
using Material.Icons;

namespace Kiriha.Converters;

public class StarKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string paramStr = parameter as string ?? "";
        string[] parts = paramStr.Split(':');
        
        if (parts.Length == 0 || !int.TryParse(parts[0], out int starIndex))
            return MaterialIconKind.StarOutline;

        string context = parts.Length > 1 ? parts[1] : "MyList";
        int starCount = 0;

        if (value == null) return MaterialIconKind.StarOutline;

        if (value is RatingOption option)
        {
            starCount = option.StarCount;
        }
        else if (value is string scoreStr)
        {
            starCount = RatingHelper.GetStarCount(scoreStr, context);
        }

        return starCount >= starIndex ? MaterialIconKind.Star : MaterialIconKind.StarOutline;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
