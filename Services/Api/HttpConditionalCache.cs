using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Serilog;

namespace Kiriha.Services.Api;

/// <summary>
/// Result envelope for <see cref="HttpConditionalCache.SendAsync"/>.
/// </summary>
/// <param name="Body">Response body bytes, or <c>null</c> on hard failure.</param>
/// <param name="StatusCode">HTTP status of the live response. <c>null</c> when
/// the body comes from a cache replay (304 hit or stale-on-error fallback).</param>
/// <param name="FromCache"><c>true</c> when <see cref="Body"/> was replayed
/// from the persisted cache (either via 304 or stale-on-error).</param>
public readonly record struct HttpCacheResult(byte[]? Body, HttpStatusCode? StatusCode, bool FromCache);

/// <summary>
/// Reusable HTTP conditional-GET wrapper backed by
/// <see cref="HttpCacheEntry"/> (table <c>http_response_cache</c>, 30 d TTL).
///
/// Usage: build a fresh <see cref="HttpRequestMessage"/> per call (with all
/// caller-specific headers Ã¢â‚¬â€ User-Agent, auth, etc.) inside
/// <c>requestFactory</c>. The helper attaches <c>If-None-Match</c> /
/// <c>If-Modified-Since</c> from the cache, sends the request, replays the
/// cached body on <c>304 Not Modified</c>, and persists fresh <c>200</c>
/// responses for next time.
///
/// Caveat: only call for endpoints whose body is safe to replay across the
/// same auth context (no per-call user-specific fields, or fields that the
/// caller already overrides downstream from a separate user store).
///
/// Persist is fire-and-forget Ã¢â‚¬â€ a transient DB hiccup costs at most one
/// extra full payload on the next call.
/// </summary>
public sealed class HttpConditionalCache
{
    private readonly HttpClient _http;
    private readonly IHttpCacheRepository _cache;
    private readonly string _logTag;
    private readonly Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

    /// <summary>Constructs a helper bound to the given <see cref="HttpClient"/>.</summary>
    /// <param name="http">Client used to send requests. The handler may add its own
    /// retry / resilience policy; that's orthogonal to this cache.</param>
    /// <param name="cache">Repository fronting the http_response_cache table.</param>
    /// <param name="logTag">Short tag used in log messages to identify the caller
    /// (e.g. <c>"MalApi"</c>, <c>"Jikan"</c>). Saves grepping logs later.</param>
    public HttpConditionalCache(HttpClient http, IHttpCacheRepository cache, string logTag)
        : this(http, cache, logTag, static (client, request, ct) => client.SendAsync(request, ct))
    {
    }

    /// <summary>
    /// Constructs a helper with a custom send delegate. Intended for services
    /// that need request routing around <see cref="HttpClient.SendAsync"/>,
    /// such as Shikimori mirror resolution.
    /// </summary>
    public HttpConditionalCache(
        HttpClient http,
        IHttpCacheRepository cache,
        string logTag,
        Func<HttpClient, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        _http = http;
        _cache = cache;
        _logTag = logTag;
        _sendAsync = sendAsync;
    }

    /// <summary>
    /// Send a GET (or any safe verb) and return the response body bytes,
    /// replaying the cached body on 304. Returns <c>null</c> on a hard
    /// failure (4xx/5xx with no usable cache, or network error with no
    /// usable cache).
    /// </summary>
    /// <param name="requestFactory">Builds a fresh request (async to allow auth-token
    /// refresh inline). Called exactly once per invocation. Must set the absolute URI;
    /// cache key is derived from <see cref="HttpRequestMessage.RequestUri"/>.</param>
    /// <param name="throttle">Optional pre-send hook (e.g. per-service rate limiter).
    /// Awaited *after* the cache lookup but *before* the network call, so cache
    /// hits don't pay the throttle cost.</param>
    public async Task<byte[]?> SendAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null,
        CancellationToken ct = default)
    {
        var result = await SendForResultAsync(requestFactory, throttle, ct);
        return result.Body;
    }

    /// <summary>
    /// Same as <see cref="SendAsync"/> but returns the full <see cref="HttpCacheResult"/>
    /// so callers can distinguish e.g. 404 (terminal Ã¢â‚¬â€ anime not on the source)
    /// from a transient network error.
    /// </summary>
    public async Task<HttpCacheResult> SendForResultAsync(
        Func<CancellationToken, Task<HttpRequestMessage>> requestFactory,
        Func<CancellationToken, Task>? throttle = null,
        CancellationToken ct = default)
    {
        var request = await requestFactory(ct);
        var fullUrl = request.RequestUri?.ToString() ?? string.Empty;
        var urlHash = HashUrl(fullUrl);

        HttpCacheEntry? cached = null;
        try { cached = await _cache.GetAsync(urlHash); }
        catch (Exception ex) { Log.Debug(ex, "{Tag}: HTTP cache lookup failed for {Url}", _logTag, fullUrl); }

        if (cached != null)
        {
            // Both validators are independent Ã¢â‚¬â€ sending both lets the server
            // pick whichever it currently honours.
            if (!string.IsNullOrEmpty(cached.ETag))
            {
                try { request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(cached.ETag)); }
                catch { /* malformed stored ETag Ã¢â‚¬â€ ignore and refetch */ }
            }
            if (!string.IsNullOrEmpty(cached.LastModified)
                && DateTimeOffset.TryParse(cached.LastModified, out var lm))
            {
                request.Headers.IfModifiedSince = lm;
            }
        }

        if (throttle != null) await throttle(ct);

        HttpResponseMessage response;
        try
        {
            response = await _sendAsync(_http, request, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Network blip Ã¢â‚¬â€ serve stale cache rather than failing the call.
            if (cached != null)
            {
                Log.Debug(ex, "{Tag}: network error, serving stale cache for {Url}", _logTag, fullUrl);
                return new HttpCacheResult(cached.Body, null, FromCache: true);
            }
            Log.Warning(ex, "{Tag}: HttpConditionalCache send failed for {Url}", _logTag, fullUrl);
            return new HttpCacheResult(null, null, FromCache: false);
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.NotModified && cached != null)
            {
                return new HttpCacheResult(cached.Body, HttpStatusCode.NotModified, FromCache: true);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 4xx/5xx without a usable replay Ã¢â‚¬â€ propagate the failure with
                // the status code so callers can react (e.g. 404 Ã¢â€ â€™ sentinel).
                Log.Debug("{Tag}: {Url} returned {Status}", _logTag, fullUrl, response.StatusCode);
                return new HttpCacheResult(null, response.StatusCode, FromCache: false);
            }

            var body = await response.Content.ReadAsByteArrayAsync(ct);
            var etag = response.Headers.ETag?.Tag;
            var lmHeader = response.Content.Headers.LastModified?.ToString("R");

            // Persist asynchronously: caller doesn't need to wait. A failure here
            // just means the next call pays a full payload instead of 304.
            _ = Task.Run(async () =>
            {
                try { await _cache.UpsertAsync(urlHash, etag, lmHeader, body); }
                catch (Exception ex) { Log.Debug(ex, "{Tag}: failed to persist HTTP cache for {Url}", _logTag, fullUrl); }
            }, CancellationToken.None);

            return new HttpCacheResult(body, response.StatusCode, FromCache: false);
        }
        finally
        {
            response.Dispose();
        }
    }

    private static string HashUrl(string url)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(url), hash);
        return Convert.ToHexString(hash);
    }
}
