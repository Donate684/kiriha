using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Models;
using Kiriha.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Converters;

public class RatingConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string scoreStr || string.IsNullOrWhiteSpace(scoreStr)) 
            return RatingHelper.GetRatingOption("-");
            
        var useFive = App.Services.GetRequiredService<SettingsService>().Current.UI.UseFiveStarRating;
        if (!useFive) return scoreStr;

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
