using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;

namespace Kiriha.Views.Converters;

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
