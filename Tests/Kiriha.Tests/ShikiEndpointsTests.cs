using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Tests;

public sealed class ShikiEndpointsTests
{
    [Theory]
    [InlineData(ShikiMirror.One, "https://shikimori.one/api/", "https://shikimori.one/oauth/token", "https://shikimori.one/animes/", "https://shikimori.one/mangas/")]
    [InlineData(ShikiMirror.Net, "https://shikimori.net/api/", "https://shikimori.net/oauth/token", "https://shikimori.net/animes/", "https://shikimori.net/mangas/")]
    public void Urls_AreResolvedPerMirror(
        ShikiMirror mirror,
        string baseUrl,
        string tokenUrl,
        string websiteUrl,
        string mangaWebsiteUrl)
    {
        Assert.Equal(baseUrl, ShikiEndpoints.BaseUrl(mirror));
        Assert.Equal(tokenUrl, ShikiEndpoints.TokenUrl(mirror));
        Assert.Equal(websiteUrl, ShikiEndpoints.WebsiteUrl(mirror));
        Assert.Equal(mangaWebsiteUrl, ShikiEndpoints.WebsiteUrl(mirror, MediaKind.Manga));
    }

    [Theory]
    [InlineData(ShikiMirror.One)]
    [InlineData(ShikiMirror.Net)]
    public void Mirrors_AreConfiguredForOAuth(ShikiMirror mirror)
    {
        Assert.True(ShikiEndpoints.IsConfigured(mirror));
        Assert.False(string.IsNullOrWhiteSpace(ShikiEndpoints.ClientId(mirror)));
        Assert.False(string.IsNullOrWhiteSpace(ShikiEndpoints.ClientSecret(mirror)));
    }
}
