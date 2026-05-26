using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Core;

/// <summary>
/// Attached property for Image controls that loads a bitmap from a URL via
/// <see cref="ImageCacheService"/>. Unlike AsyncImageLoader's AdvancedImage,
/// this loader NEVER disposes the bitmap it hands to the Image control, so
/// ItemsRepeater recycling and rapid Source rebinding cannot race us into a
/// disposed-bitmap state.
/// </summary>
public static class CachedImage
{
    private static ImageCacheService? _imageCache;

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(CachedImage));

    static CachedImage()
    {
        SourceProperty.Changed.AddClassHandler<Image>((img, args) => OnSourceChanged(img, args));
    }

    public static void Initialize(ImageCacheService imageCache)
    {
        _imageCache = imageCache;
    }

    public static string? GetSource(Image element) => element.GetValue(SourceProperty);
    public static void SetSource(Image element, string? value) => element.SetValue(SourceProperty, value);

    private static async void OnSourceChanged(Image img, AvaloniaPropertyChangedEventArgs args)
    {
        var url = args.NewValue as string;

        if (string.IsNullOrEmpty(url))
        {
            img.Source = null;
            return;
        }

        if (_imageCache == null) return;

        try
        {
            var bmp = await _imageCache.LoadBitmapAsync(url);

            // Ensure we're still bound to the same URL by the time the load completes.
            // Avoids setting a stale bitmap if the control was recycled mid-load.
            if (GetSource(img) != url) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (GetSource(img) == url)
                {
                    img.Source = bmp;
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[CachedImage] Failed to load {Url}", url);
        }
    }
}
