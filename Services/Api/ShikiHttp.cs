using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Serilog;

namespace Kiriha.Services.Api;

/// <summary>
/// Shikimori-aware HTTP send wrapper that handles the multi-domain fork
/// (<c>.net</c>, <c>.rip</c>, <c>.fi</c>, …).
/// 
/// <para>
/// Two failure modes are observed in the wild — depending on the user's IP,
/// either the server 301/302-redirects to the "right" Shikimori domain, or it
/// silently returns 404 from the wrong domain without any redirect at all
/// (regional blocking circumvention is implemented inconsistently).
/// </para>
/// 
/// <para>
/// We solve both with a single send helper:
///   <list type="number">
///     <item>Auto-redirect on the underlying HttpClient is OFF (see DI registration);
///     we follow 3xx manually so that POST stays POST, the request body is
///     re-sent, and Authorization survives the cross-host hop — none of which
///     <see cref="HttpClient"/> does by default. Any
///     <c>shikimori.&lt;tld&gt;</c> Location is accepted on the spot, so the
///     fork can spin up new domains tomorrow without a code change.</item>
///     <item>On a 404 from an API path with no redirect, we walk
///     <see cref="ShikiHostResolver.ForkProbeOrder"/> and retry against each
///     known fork host until one answers with anything other than 404.
///     The first success is pinned for the rest of the session.</item>
///   </list>
/// </para>
/// 
/// <para>
/// Successful host swaps are pinned in <see cref="ShikiHostResolver"/> for the
/// rest of the session so subsequent calls go straight to the working host.
/// </para>
/// </summary>
internal static class ShikiHttp
{
    public static async Task<HttpResponseMessage> SendShikiAsync(
        HttpClient client,
        HttpRequestMessage request,
        ShikiHostResolver resolver,
        CancellationToken ct,
        int maxHops = 3)
    {
        // Apply any session-pinned host BEFORE the first send so we don't
        // pay the redirect/404 round-trip again after the first discovery.
        request.RequestUri = resolver.Rewrite(request.RequestUri!);

        var aliasTried = false;
        for (var hop = 0; hop <= maxHops; hop++)
        {
            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            var code = (int)response.StatusCode;

            // ── Scenario A: explicit redirect ────────────────────────────────
            if (code is 301 or 302 or 307 or 308 && response.Headers.Location is not null)
            {
                var target = new Uri(request.RequestUri!, response.Headers.Location);

                // Only follow Shikimori → Shikimori redirects within the same
                // realm. Anything else (e.g. .one → .net) is a misconfiguration
                // we MUST NOT silently fix up — tokens wouldn't transfer.
                if (!ShikiHostResolver.IsShikiHost(target.Host))
                {
                    return response;
                }
                if (!resolver.IsSameRealm(request.RequestUri!.Host, target.Host))
                {
                    Log.Warning("Shiki cross-realm redirect rejected: {From} -> {To}",
                        request.RequestUri.Host, target.Host);
                    return response;
                }

                resolver.Remember(request.RequestUri!.Host, target.Host);
                Log.Information("Shiki host pinned via redirect: {From} -> {To}",
                    request.RequestUri.Host, target.Host);

                response.Dispose();
                request = await CloneRequestAsync(request, target, ct).ConfigureAwait(false);
                continue;
            }

            // ── Scenario B: silent 404 on an API path ────────────────────────
            // Walk the known fork hosts and retry against each. The first one
            // that answers with anything but 404 wins and gets pinned. If they
            // ALL 404, it's a genuine "not found" and we surface the last
            // response. Heuristic guard: only act on /api/* and /oauth/* so a
            // genuine 404 on, say, /assets/* can't trigger unrelated retries.
            if (code == 404 && !aliasTried &&
                LooksLikeApiPath(request.RequestUri!.AbsolutePath) &&
                resolver.IsKnownHost(request.RequestUri.Host))
            {
                aliasTried = true;
                var originalHost = request.RequestUri.Host;
                response.Dispose();

                HttpResponseMessage? lastResponse = null;
                foreach (var alias in resolver.ProbeOrder(originalHost))
                {
                    var target = new UriBuilder(request.RequestUri) { Host = alias }.Uri;
                    Log.Information("Shiki 404 on {Host}, probing alias {Alias}",
                        originalHost, alias);

                    var probeRequest = await CloneRequestAsync(request, target, ct).ConfigureAwait(false);
                    var probeResponse = await client.SendAsync(probeRequest, ct).ConfigureAwait(false);

                    if ((int)probeResponse.StatusCode != 404)
                    {
                        if (resolver.Remember(originalHost, alias))
                        {
                            Log.Information("Shiki host pinned via 404-fallback: {From} -> {To}",
                                originalHost, alias);
                        }
                        lastResponse?.Dispose();
                        return probeResponse;
                    }

                    lastResponse?.Dispose();
                    lastResponse = probeResponse;
                }

                // Every known fork host returned 404. Surface the last one as
                // the "real" response — caller will treat it as a normal 404.
                return lastResponse ?? response;
            }

            return response;
        }

        throw new HttpRequestException("Too many Shikimori redirects.");
    }

    /// <summary>
    /// Builds a fresh <see cref="HttpRequestMessage"/> for <paramref name="newUri"/>
    /// preserving method, headers (including <c>Authorization</c>) and a buffered
    /// copy of the body. Required because (a) HttpClient consumes the original
    /// request after one send, (b) its built-in redirect logic strips the body
    /// on 301/302 and the Authorization header on cross-host hops.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage original, Uri newUri, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(original.Method, newUri);

        // Copy request-level headers verbatim. TryAddWithoutValidation is used
        // because the Authorization header has already been validated upstream
        // and we don't want re-validation to drop it on the floor.
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Buffer the request body once so it can be replayed across hops.
        // FormUrlEncodedContent / StringContent are small (<1KB) so the byte
        // array round-trip is negligible.
        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var newContent = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
            {
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Content = newContent;
        }

        // Carry over options/version so the new request behaves identically.
        clone.Version = original.Version;
        clone.VersionPolicy = original.VersionPolicy;
        return clone;
    }

    private static bool LooksLikeApiPath(string path) =>
        path.StartsWith("/api/", StringComparison.Ordinal) ||
        path.StartsWith("/oauth/", StringComparison.Ordinal);
}
