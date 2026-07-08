using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;

namespace Kiriha.Views.Converters;

public class HistoryActionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int typeId) return value;

        string? param = parameter?.ToString();

        if (param == "icon")
        {
            return typeId switch
            {
                1 => "CheckCircleOutline",   // Watched
                2 => "History",              // Reverted
                3 => "AlertCircleOutline",   // SyncFailed
                4 => "Broadcast",            // Scrobbled
                5 => "StarOutline",          // ScoreSet
                6 => "TrophyOutline",         // Completed
                7 => "CloseCircleOutline",    // Dropped
                _ => "InformationOutline"
            };
        }

        // Text localization
        string key = typeId switch
        {
            1 => "history.actions.watched",
            2 => "history.actions.reverted",
            3 => "history.actions.sync_failed",
            4 => "history.actions.scrobbled",
            5 => "history.actions.score_set",
            6 => "history.actions.completed",
            7 => "history.actions.dropped",
            _ => "common.status.unknown"
        };

        return UIUtils.GetLoc(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
