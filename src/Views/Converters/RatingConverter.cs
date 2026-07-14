using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Models;

namespace Kiriha.Views.Converters;

public class RatingConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string scoreStr || string.IsNullOrWhiteSpace(scoreStr)) 
            return RatingHelper.GetRatingOption("-");

        return RatingHelper.GetRatingOption(scoreStr);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RatingOption option)
        {
            return option.Value;
        }
        return "0";
    }
}
