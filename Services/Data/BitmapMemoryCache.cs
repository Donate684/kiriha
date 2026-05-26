using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Kiriha.Services.Data;

/// <summary>
/// Two-tier in-memory cache layered on top of the disk image cache used by
/// <see cref="ImageCacheService"/>.
///
///   L1 (pixel cache, ~16 MB)
///     Decoded BGRA pixel buffers keyed by (path, decodeWidth).
///     Hit  => allocate a fresh <see cref="WriteableBitmap"/> and copy pixels in.
///     Cost ~1 ms (memcpy + GPU upload). No JPEG decode, no disk I/O.
///
///   L2 (encoded bytes cache, ~32 MB)
///     Raw on-disk file bytes keyed by path.
///     Hit  => decode from <see cref="System.IO.MemoryStream"/> into a fresh
///             <see cref="Bitmap"/>, then promote its pixels to L1.
///     Cost ~3-10 ms (decode). No disk I/O.
///
/// Every call returns an INDEPENDENT bitmap instance. This is intentional:
/// AsyncImageLoader's AdvancedImage disposes the "previous" Source on rebind
/// (recycling in ItemsRepeater), so any shared instance would die on the
/// neighbour cards and render blank. See the long-form note in
/// <see cref="ImageCacheService.LoadBitmapAsync"/>.
/// </summary>
public sealed class BitmapMemoryCache
{
    private readonly ByteSizedLru<string, byte[]> _encoded;
    private readonly ByteSizedLru<PixelKey, PixelEntry> _pixels;

    public BitmapMemoryCache(long encodedBudgetBytes = 32L * 1024 * 1024,
                              long pixelBudgetBytes  = 16L * 1024 * 1024)
    {
        _encoded = new ByteSizedLru<string, byte[]>(encodedBudgetBytes, b => b.Length);
        _pixels  = new ByteSizedLru<PixelKey, PixelEntry>(pixelBudgetBytes, p => p.Pixels.Length);
    }

    /// <summary>
    /// L1 hit path: rent an independent WriteableBitmap built from cached pixels.
    /// Returns false on miss or on any failure during materialization (caller
    /// should then fall through to the encoded/disk path).
    /// </summary>
    public bool TryRentBitmap(string path, int decodeWidth, out Bitmap? bitmap)
    {
        bitmap = null;
        if (!_pixels.TryGet(new PixelKey(path, decodeWidth), out var entry) || entry == null)
            return false;

        GCHandle handle = default;
        try
        {
            var wb = new WriteableBitmap(entry.Size, new Vector(96, 96), entry.Format, entry.AlphaFormat);
            using (var fb = wb.Lock())
            {
                Marshal.Copy(entry.Pixels, 0, fb.Address, entry.Pixels.Length);
            }
            bitmap = wb;
            return true;
        }
        catch
        {
            // GPU upload failures, OOM, format mismatch Ã¢â‚¬â€ fall back to a fresh decode.
            return false;
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    public bool TryGetEncoded(string path, out byte[]? bytes) => _encoded.TryGet(path, out bytes);

    public void StoreEncoded(string path, byte[] bytes) => _encoded.Set(path, bytes);

    public void Clear()
    {
        _encoded.Clear();
        _pixels.Clear();
    }

    /// <summary>
    /// Extracts pixels from a freshly-decoded Bitmap and promotes them to L1.
    /// Best-effort: silently no-ops if the source format is unknown or pixel
    /// extraction fails Ã¢â‚¬â€ the encoded-bytes layer still gives most of the win.
    /// </summary>
    public void StorePixelsFrom(string path, int decodeWidth, Bitmap bmp)
    {
        if (bmp.Format is not { } fmt) return;
        var alpha = bmp.AlphaFormat ?? AlphaFormat.Premul;

        var size = bmp.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return;

        int bpp    = (fmt.BitsPerPixel + 7) / 8;
        int stride = size.Width * bpp;
        int total  = stride * size.Height;
        if (total <= 0) return;

        var buffer = new byte[total];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, size.Width, size.Height),
                           handle.AddrOfPinnedObject(), total, stride);
        }
        catch
        {
            return;
        }
        finally
        {
            handle.Free();
        }

        _pixels.Set(new PixelKey(path, decodeWidth),
                    new PixelEntry(buffer, size, stride, fmt, alpha));
    }

    private readonly record struct PixelKey(string Path, int DecodeWidth);

    private sealed class PixelEntry
    {
        public PixelEntry(byte[] pixels, PixelSize size, int stride, PixelFormat fmt, AlphaFormat alpha)
        {
            Pixels = pixels; Size = size; Stride = stride; Format = fmt; AlphaFormat = alpha;
        }
        public byte[] Pixels { get; }
        public PixelSize Size { get; }
        public int Stride { get; }
        public PixelFormat Format { get; }
        public AlphaFormat AlphaFormat { get; }
    }

    /// <summary>
    /// Generic byte-sized LRU. Single-mutex; lookups are O(1) and keep the cache
    /// hot-path on a couple of dictionary + linked-list operations. Items larger
    /// than the entire budget are silently dropped instead of evicting everything
    /// on a single insert.
    /// </summary>
    private sealed class ByteSizedLru<TKey, TVal>
        where TKey : notnull
        where TVal : class
    {
        private readonly long _budget;
        private readonly Func<TVal, long> _sizer;
        private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
        private readonly LinkedList<Entry> _order = new();
        private readonly object _gate = new();
        private long _used;

        public ByteSizedLru(long budgetBytes, Func<TVal, long> sizer)
        {
            _budget = budgetBytes;
            _sizer = sizer;
        }

        public bool TryGet(TKey key, out TVal? value)
        {
            lock (_gate)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
                value = null;
                return false;
            }
        }

        public void Set(TKey key, TVal value)
        {
            long size = _sizer(value);
            if (size <= 0 || size > _budget) return;

            lock (_gate)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    _used -= _sizer(existing.Value.Value);
                    _order.Remove(existing);
                    _map.Remove(key);
                }

                var node = new LinkedListNode<Entry>(new Entry(key, value));
                _order.AddFirst(node);
                _map[key] = node;
                _used += size;

                while (_used > _budget && _order.Last is { } tail)
                {
                    _used -= _sizer(tail.Value.Value);
                    _map.Remove(tail.Value.Key);
                    _order.RemoveLast();
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _map.Clear();
                _order.Clear();
                _used = 0;
            }
        }

        private readonly record struct Entry(TKey Key, TVal Value);
    }
}
