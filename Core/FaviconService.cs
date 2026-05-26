using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Core;

/// <summary>
/// Best-effort favicon downloader for user-defined custom share buttons.
/// Given a URL template (which may contain placeholders like {title}), this
/// extracts the host, attempts to download <c>/favicon.ico</c> and
/// <c>/favicon.png</c> from the site root, and caches the result locally
/// under <see cref="PathHelper.GetCustomIconsPath"/>. Returns the local file
/// path on success, or <c>null</c> when nothing usable was retrieved Ã¢â‚¬â€ in
/// that case the UI falls back to the default globe icon.
///
/// Cache key is the host name, so two custom links pointing at the same site
/// reuse the same icon file. This file is NOT keyed by link id, so removing
/// a link must NOT delete the icon (other links may still rely on it).
/// </summary>
public static class FaviconService
{
    // Reasonable defaults: short timeout so a slow / dead host doesn't keep
    // a debounced fetch alive forever, and a small concurrency cap so we
    // don't fire dozens of requests if the user pastes many URLs in a row.
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    private static readonly SemaphoreSlim _gate = new(4);

    /// <summary>
    /// Tries to fetch a favicon for the host of <paramref name="urlTemplate"/>.
    /// Placeholders in the template are tolerated Ã¢â‚¬â€ they're stripped before
    /// parsing so e.g. <c>https://x.com/?q={title}</c> still yields host
    /// <c>x.com</c>.
    /// </summary>
    public static async Task<string?> TryGetFaviconAsync(string urlTemplate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(urlTemplate)) return null;

        // Replace common placeholders with safe stand-ins so Uri parsing succeeds.
        var cleaned = urlTemplate
            .Replace("{title}", "x")
            .Replace("{english}", "x")
            .Replace("{russian}", "x")
            .Replace("{japanese}", "x")
            .Replace("{id}", "1")
            .Replace("{malId}", "1")
            .Replace("{shikiId}", "1");

        if (!Uri.TryCreate(cleaned, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.Host;
        if (string.IsNullOrEmpty(host)) return null;

        var dir = PathHelper.GetCustomIconsPath();
        try { Directory.CreateDirectory(dir); } catch { /* best-effort */ }

        // Sanitize host into a stable filename component.
        var safeHost = SanitizeHost(host);

        // Reuse cached file if we already grabbed one for this host.
        var existing = TryFindCached(dir, safeHost);
        if (existing != null) return existing;

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check after acquiring (another fetch may have completed).
            existing = TryFindCached(dir, safeHost);
            if (existing != null) return existing;

            // Try the well-known root paths in order. /favicon.ico is the
            // historical default; /favicon.png is the modern fallback.
            foreach (var ext in new[] { "ico", "png" })
            {
                var candidate = $"{uri.Scheme}://{host}/favicon.{ext}";
                try
                {
                    using var resp = await _http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode) continue;

                    var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                    // Sites often return a 200 HTML error page for missing
                    // favicons. A real icon is at least a few hundred bytes
                    // and never starts with '<' (HTML).
                    if (bytes.Length < 64) continue;
                    if (bytes[0] == (byte)'<') continue;

                    var dest = Path.Combine(dir, safeHost + "." + ext);
                    await File.WriteAllBytesAsync(dest, bytes, ct);
                    return dest;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Favicon fetch failed for {Url}", candidate);
                }
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? TryFindCached(string dir, string safeHost)
    {
        try
        {
            return Directory.EnumerateFiles(dir, safeHost + ".*").FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeHost(string host)
    {
        var chars = host.ToLowerInvariant().Select(c =>
            char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_').ToArray();
        return new string(chars);
    }
}
