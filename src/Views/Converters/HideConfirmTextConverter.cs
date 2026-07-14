using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;

namespace Kiriha.Views.Converters;

/// <summary>
/// Picks the right localized confirmation label for the seasonal hide button:
/// "Hide?" / "???????" when the title is not yet hidden,
/// "Restore?" / "????????" when un-hiding.
/// </summary>
public class HideConfirmTextConverter : IMultiValueConverter
{
    public static readonly HideConfirmTextConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isHidden = values is { Count: > 0 } && values[0] is bool b && b;
        return UIUtils.GetLoc(isHidden
            ? "filters.options.unhide_confirm"
            : "filters.options.hide_confirm");
    }
}
