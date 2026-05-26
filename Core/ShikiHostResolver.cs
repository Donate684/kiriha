using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kiriha.Core;

/// <summary>
/// Per-session cache of "which Shikimori host actually answers right now" for
/// the fork realm. The Shikimori fan fork keeps adding new domains to dodge
/// regional blocks (<c>.net</c>, <c>.rip</c>, <c>.fi</c>, Ã¢â‚¬Â¦) Ã¢â‚¬â€ all serve the
/// same backend, all share OAuth tokens. <see cref="ShikiHttp"/> probes for
/// a working host on first failure and pins the result here so subsequent
/// calls skip the discovery round-trip.
///
/// <para>
/// <b>Future-proofing.</b> Recognition is regex-based
/// (<c>shikimori.&lt;tld&gt;</c>), so a brand-new fork domain that we learn
/// about via a redirect Location header is accepted without a code change.
/// The hard-coded <see cref="KnownForkHosts"/> list only seeds the
/// <em>404-fallback probe order</em> Ã¢â‚¬â€ when the server doesn't redirect at
/// all and we need to guess. Adding a new domain to that list is a single
/// line change.
/// </para>
///
/// <para>
/// <see cref="ShikiMirror.One"/> (<c>shikimori.one</c>) is a SEPARATE OAuth
/// realm and is never aliased into the fork Ã¢â‚¬â€ tokens minted for one realm
/// are worthless on the other.
/// </para>
/// </summary>
public sealed class ShikiHostResolver
{
    // Anything that *looks like* shikimori.<tld>. We trust this for redirect
    // Locations (the server itself is telling us where to go) but never for
    // arbitrary user input Ã¢â‚¬â€ the only place this is consulted is inside
    // SendShikiAsync, which already gates on the request having originated
    // from a Shiki host of its own.
    private static readonly Regex ShikiHostPattern =
        new(@"^shikimori\.[a-z]{2,6}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string OriginalHost = "shikimori.one";

    /// <summary>
    /// Known fork hosts to try when the server returns a silent 404 (no
    /// redirect Location to follow). Order = probe order. Adding a new domain
    /// here is the only code change needed when the fork spawns yet another
    /// circumvention domain Ã¢â‚¬â€ recognition via <see cref="ShikiHostPattern"/>
    /// already handles redirects to any new <c>shikimori.&lt;tld&gt;</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> KnownForkHosts = new[]
    {
        "shikimori.rip",
        "shikimori.net",
        "shikimori.fi",
    };

    // Active host for the fork realm. null = no pin yet, use whatever the
    // caller asked for (will be probed by ShikiHttp on first failure).
    // The original (.one) realm has no alias so doesn't need a slot.
    private readonly object _gate = new();
    private string? _activeForkHost;

    /// <summary>
    /// Returns <paramref name="original"/> with its host swapped to the
    /// realm's currently active host. No-op if the URI's host is already
    /// active, the realm has no pin, or the URI is in the original realm.
    /// </summary>
    public Uri Rewrite(Uri original)
    {
        if (!IsForkHost(original.Host)) return original;

        string? active;
        lock (_gate) active = _activeForkHost;

        if (active is null ||
            string.Equals(active, original.Host, StringComparison.OrdinalIgnoreCase))
        {
            return original;
        }
        return new UriBuilder(original) { Host = active }.Uri;
    }

    /// <summary>
    /// Records that <paramref name="toHost"/> is the working host for its
    /// realm. Replaces any previous pin Ã¢â‚¬â€ this is what makes VPN toggling
    /// cheap: every successful re-discovery costs exactly the one round-trip
    /// that found it, and never again until the next switch.
    /// Returns false (no-op) for non-fork hosts or cross-realm pairs.
    /// </summary>
    public bool Remember(string fromHost, string toHost)
    {
        if (!IsSameRealm(fromHost, toHost)) return false;
        if (!IsForkHost(toHost))            return false; // .one has no alias

        lock (_gate) _activeForkHost = toHost;
        return true;
    }

    /// <summary>Forgets the session pin. Called on mirror switch in settings.</summary>
    public void Reset()
    {
        lock (_gate) _activeForkHost = null;
    }

    /// <summary>The currently pinned fork host, or null if none. For diagnostics.</summary>
    public string? ActiveForkHost
    {
        get { lock (_gate) return _activeForkHost; }
    }

    /// <summary>True for any host matching <c>shikimori.&lt;tld&gt;</c>.</summary>
    public static bool IsShikiHost(string host) => ShikiHostPattern.IsMatch(host);

    /// <summary>True for the original Shikimori site (<c>shikimori.one</c>).</summary>
    public static bool IsOriginalHost(string host) =>
        string.Equals(host, OriginalHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True for any Shikimori host that is NOT the original Ã¢â‚¬â€ i.e. any fork
    /// domain (<c>.net</c>, <c>.rip</c>, <c>.fi</c>, and whatever they invent
    /// next). Trusts the regex; the caller must already have established
    /// that the host came from a legitimate Shikimori interaction.
    /// </summary>
    public static bool IsForkHost(string host) =>
        ShikiHostPattern.IsMatch(host) && !IsOriginalHost(host);

    /// <summary>
    /// True when both hosts belong to the same OAuth realm. Fork hosts are
    /// all one realm with each other; .one is a realm of its own.
    /// </summary>
    public static bool IsSameRealm(string a, string b)
    {
        if (IsOriginalHost(a) && IsOriginalHost(b)) return true;
        if (IsForkHost(a)     && IsForkHost(b))     return true;
        return false;
    }

    /// <summary>
    /// Yields known fork hosts to try on a silent 404, skipping
    /// <paramref name="excluding"/> (the host that already failed).
    /// Used by <see cref="ShikiHttp"/> when there's no redirect Location
    /// to follow and we have to guess at a working alias.
    /// </summary>
    public static IEnumerable<string> ForkProbeOrder(string excluding)
    {
        foreach (var host in KnownForkHosts)
        {
            if (!string.Equals(host, excluding, StringComparison.OrdinalIgnoreCase))
                yield return host;
        }
    }
}
