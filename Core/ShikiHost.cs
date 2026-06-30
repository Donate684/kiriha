namespace Kiriha.Core;

/// <summary>
/// Endpoint bundle for one Shikimori OAuth realm (shikimori.one or shikimori.net).
/// The two realms have identical contracts but issue mutually-incompatible tokens,
/// so the active host is resolved per-call from the user's selected mirror.
/// See <see cref="ShikiEndpoints"/> for the resolver.
/// </summary>
public sealed record ShikiHost(
    string BaseUrl,
    string TokenUrl,
    string AuthUrl,
    string WebsiteUrl,
    string MangaWebsiteUrl);
