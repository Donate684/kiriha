using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Kiriha.Converters;

/// <summary>
/// Loads a <see cref="Bitmap"/> from a local file path. Used by the custom
/// share link icon previews so XAML can bind directly to a string path.
/// Returns <c>null</c> for empty / missing paths so <c>Image.Source</c>
/// falls through cleanly.
/// </summary>
public class FilePathToBitmapConverter : IValueConverter
{
    public static readonly FilePathToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        if (!File.Exists(path)) return null;
        try
        {
            // Stream-based ctor avoids holding a file handle on the icon file
            // after the bitmap is decoded — important because the user may
            // overwrite or delete the file later.
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
