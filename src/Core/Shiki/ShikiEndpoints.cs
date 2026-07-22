using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Core.Shiki;

/// <summary>
/// Resolves Shikimori endpoints + OAuth client_id for the active mirror.
/// shikimori.one and shikimori.net are independent OAuth realms, so every
/// outbound request must read the *current* mirror from settings.
/// </summary>
public static class ShikiEndpoints
{
    /// <summary>
    /// Resolves the endpoint bundle (URLs only — no secrets) for the active mirror.
    /// All four URL accessors below funnel through this single switch so adding a
    /// future mirror or accessor is a one-line change.
    /// </summary>
    public static ShikiHost Host(ShikiMirror mirror) => mirror switch
    {
        ShikiMirror.Net => Constants.Api.Shiki.Net,
        _ => Constants.Api.Shiki.One,
    };

    public static string BaseUrl(ShikiMirror mirror) => Host(mirror).BaseUrl;
    public static string TokenUrl(ShikiMirror mirror) => Host(mirror).TokenUrl;
    public static string AuthUrl(ShikiMirror mirror) => Host(mirror).AuthUrl;
    public static string WebsiteUrl(ShikiMirror mirror, MediaKind mediaKind = MediaKind.Anime)
    {
        var host = Host(mirror);
        return mediaKind == MediaKind.Anime ? host.WebsiteUrl : host.MangaWebsiteUrl;
    }

    public static string ClientId(ShikiMirror mirror) => mirror switch
    {
        ShikiMirror.Net => ApiKeys.ShikiNetClientId,
        _ => ApiKeys.ShikiClientId,
    };

    /// <summary>
    /// Returns the embedded client_secret for the requested mirror. Both Shikimori
    /// mirrors call /oauth/token directly from the user's machine (see ApiKeys.cs
    /// for why), so the secret always travels with the request body.
    /// </summary>
    public static string ClientSecret(ShikiMirror mirror) => mirror switch
    {
        ShikiMirror.Net => ApiKeys.ShikiNetClientSecret,
        _ => ApiKeys.ShikiClientSecret,
    };

    /// <summary>True only when the OAuth pipeline is fully configured for this mirror.</summary>
    public static bool IsConfigured(ShikiMirror mirror) =>
        !string.IsNullOrEmpty(ClientId(mirror)) && !string.IsNullOrEmpty(TokenUrl(mirror));
}
