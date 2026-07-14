using System;
using System.Threading.Tasks;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia.Media.Imaging;
using Serilog;

namespace Kiriha.Services.Data;

public class KirihaImageLoader : IAsyncImageLoader
{
    private readonly ImageCacheService _imageCache;

    public KirihaImageLoader(ImageCacheService imageCache)
    {
        _imageCache = imageCache;
    }

    public async Task<Bitmap?> ProvideImageAsync(string url)
    {
        try
        {
            return await _imageCache.LoadBitmapAsync(url);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[ImageLoad] Failed to load {Url}", url);
            return null;
        }
    }

    public void Dispose()
    {
        // Disposal of ImageCacheService is handled by DI container.
    }
}
