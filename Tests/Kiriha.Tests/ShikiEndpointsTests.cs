using Kiriha.Core;
using Kiriha.Models;

namespace Kiriha.Tests;

public sealed class ShikiEndpointsTests
{
    [Theory]
    [InlineData(ShikiMirror.One, "https://shikimori.one/api/", "https://shikimori.one/oauth/token", "https://shikimori.one/animes/")]
    [InlineData(ShikiMirror.Net, "https://shikimori.net/api/", "https://shikimori.net/oauth/token", "https://shikimori.net/animes/")]
    public void Urls_AreResolvedPerMirror(
        ShikiMirror mirror,
        string baseUrl,
        string tokenUrl,
        string websiteUrl)
    {
        Assert.Equal(baseUrl, ShikiEndpoints.BaseUrl(mirror));
        Assert.Equal(tokenUrl, ShikiEndpoints.TokenUrl(mirror));
        Assert.Equal(websiteUrl, ShikiEndpoints.WebsiteUrl(mirror));
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
