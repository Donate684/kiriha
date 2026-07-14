using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Kiriha.Core.Shiki;

/// <summary>
/// Per-session cache of "which Shikimori host actually answers right now" for
/// both the Original and Fork realms. Shikimori mirrors (both original and fork)
/// keep adding new domains to dodge regional blocks (<c>.net</c>, <c>.rip</c>,
/// <c>.io</c>, …). <see cref="ShikiHttp"/> probes for a working host on first
/// failure or 301 redirect and pins the result here so subsequent calls skip
/// the discovery round-trip.
///
/// <para>
/// <b>Future-proofing.</b> Recognition is regex-based
/// (<c>shikimori.&lt;tld&gt;</c>), so a brand-new domain that we learn
/// about via a redirect Location header is accepted without a code change.
/// The hard-coded <see cref="KnownForkHosts"/> and <see cref="KnownOriginalHosts"/>
/// lists only seed the <em>404-fallback probe order</em> — when the server doesn't
/// redirect at all and we need to guess.
/// </para>
///
/// <para>
/// <see cref="ShikiMirror.One"/> (<c>shikimori.one</c>) and <see cref="ShikiMirror.Net"/> (<c>shikimori.net</c>)
/// are SEPARATE OAuth realms and are never aliased into each other — tokens minted for one
/// realm are worthless on the other.
/// </para>
/// </summary>
public sealed class ShikiHostResolver
{
    // Anything that *looks like* shikimori.<tld>. We trust this for redirect
    // Locations (the server itself is telling us where to go) but never for
    // arbitrary user input — the only place this is consulted is inside
    // SendShikiAsync, which already gates on the request having originated
    // from a Shiki host of its own.
    private static readonly Regex ShikiHostPattern =
        new(@"^shikimori\.[a-z]{2,6}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Known original hosts to try when the server returns a silent 404 (no
    /// redirect Location to follow).
    /// </summary>
    public static readonly IReadOnlyList<string> KnownOriginalHosts = new[]
    {
        "shikimori.one",
        "shikimori.io"
    };

    /// <summary>
    /// Known fork hosts to try when the server returns a silent 404 (no
    /// redirect Location to follow).
    /// </summary>
    public static readonly IReadOnlyList<string> KnownForkHosts = new[]
    {
        "shikimori.rip",
        "shikimori.net",
        "shikimori.fi",
    };

    private readonly object _gate = new();

    private readonly HashSet<string> _originalHosts = new(KnownOriginalHosts, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _forkHosts = new(KnownForkHosts, StringComparer.OrdinalIgnoreCase);

    private string? _activeOriginalHost;
    private string? _activeForkHost;

    /// <summary>
    /// Returns <paramref name="original"/> with its host swapped to the
    /// realm's currently active host. No-op if the URI's host is already
    /// active, or the realm has no pin.
    /// </summary>
    public Uri Rewrite(Uri original)
    {
        lock (_gate)
        {
            if (_originalHosts.Contains(original.Host))
            {
                if (_activeOriginalHost != null && !string.Equals(_activeOriginalHost, original.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return new UriBuilder(original) { Host = _activeOriginalHost }.Uri;
                }
            }
            else if (_forkHosts.Contains(original.Host))
            {
                if (_activeForkHost != null && !string.Equals(_activeForkHost, original.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return new UriBuilder(original) { Host = _activeForkHost }.Uri;
                }
            }
        }
        return original;
    }

    /// <summary>
    /// Records that <paramref name="toHost"/> is the working host for its
    /// realm. Replaces any previous pin — this is what makes VPN toggling
    /// cheap: every successful re-discovery costs exactly the one round-trip
    /// that found it, and never again until the next switch.
    /// Returns false (no-op) for cross-realm pairs.
    /// </summary>
    public bool Remember(string fromHost, string toHost)
    {
        lock (_gate)
        {
            if (!IsSameRealmInternal(fromHost, toHost)) return false;

            if (_originalHosts.Contains(fromHost))
            {
                _originalHosts.Add(toHost);
                _activeOriginalHost = toHost;
                return true;
            }
            
            if (_forkHosts.Contains(fromHost))
            {
                _forkHosts.Add(toHost);
                _activeForkHost = toHost;
                return true;
            }
        }
        return false;
    }

    /// <summary>Forgets the session pin. Called on mirror switch in settings.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _activeOriginalHost = null;
            _activeForkHost = null;
        }
    }

    /// <summary>The currently pinned fork host, or null if none. For diagnostics.</summary>
    public string? ActiveForkHost
    {
        get { lock (_gate) return _activeForkHost; }
    }

    /// <summary>The currently pinned original host, or null if none. For diagnostics.</summary>
    public string? ActiveOriginalHost
    {
        get { lock (_gate) return _activeOriginalHost; }
    }

    /// <summary>True for any host matching <c>shikimori.&lt;tld&gt;</c>.</summary>
    public static bool IsShikiHost(string host) => ShikiHostPattern.IsMatch(host);

    /// <summary>True for any host belonging to the Original Shikimori realm.</summary>
    public bool IsOriginalHost(string host)
    {
        lock (_gate) return _originalHosts.Contains(host);
    }

    /// <summary>True for any host belonging to the Fork Shikimori realm.</summary>
    public bool IsForkHost(string host)
    {
        lock (_gate) return _forkHosts.Contains(host);
    }

    /// <summary>True if the host is known to either realm.</summary>
    public bool IsKnownHost(string host)
    {
        lock (_gate) return _originalHosts.Contains(host) || _forkHosts.Contains(host);
    }

    /// <summary>
    /// True when both hosts belong to the same OAuth realm.
    /// </summary>
    public bool IsSameRealm(string a, string b)
    {
        lock (_gate) return IsSameRealmInternal(a, b);
    }

    private bool IsSameRealmInternal(string a, string b)
    {
        bool aIsOriginal = _originalHosts.Contains(a);
        bool aIsFork = _forkHosts.Contains(a);

        bool bIsOriginal = _originalHosts.Contains(b);
        bool bIsFork = _forkHosts.Contains(b);

        if (aIsOriginal && bIsOriginal) return true;
        if (aIsFork && bIsFork) return true;

        if (aIsOriginal && !bIsFork && IsShikiHost(b)) return true;
        if (aIsFork && !bIsOriginal && IsShikiHost(b)) return true;

        return false;
    }

    /// <summary>
    /// Yields known hosts to try on a silent 404 for the same realm as <paramref name="excluding"/>,
    /// skipping <paramref name="excluding"/> (the host that already failed).
    /// Used by <see cref="ShikiHttp"/> when there's no redirect Location
    /// to follow and we have to guess at a working alias.
    /// </summary>
    public IEnumerable<string> ProbeOrder(string excluding)
    {
        IReadOnlyList<string> knownHosts;
        lock (_gate)
        {
            if (_originalHosts.Contains(excluding)) knownHosts = KnownOriginalHosts;
            else if (_forkHosts.Contains(excluding)) knownHosts = KnownForkHosts;
            else yield break;
        }

        foreach (var host in knownHosts)
        {
            if (!string.Equals(host, excluding, StringComparison.OrdinalIgnoreCase))
                yield return host;
        }
    }
}
