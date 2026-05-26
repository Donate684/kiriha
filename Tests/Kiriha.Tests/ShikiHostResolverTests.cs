using Kiriha.Core;

namespace Kiriha.Tests;

public sealed class ShikiHostResolverTests
{
    [Theory]
    [InlineData("shikimori.one", true, false)]
    [InlineData("shikimori.net", false, true)]
    [InlineData("shikimori.rip", false, true)]
    [InlineData("shikimori.fi", false, true)]
    [InlineData("example.com", false, false)]
    public void HostClassification_DistinguishesOriginalAndForkRealms(
        string host,
        bool isOriginal,
        bool isFork)
    {
        Assert.Equal(isOriginal || isFork, ShikiHostResolver.IsShikiHost(host));
        Assert.Equal(isOriginal, ShikiHostResolver.IsOriginalHost(host));
        Assert.Equal(isFork, ShikiHostResolver.IsForkHost(host));
    }

    [Fact]
    public void Remember_PinsOnlyWithinForkRealm()
    {
        var resolver = new ShikiHostResolver();

        Assert.False(resolver.Remember("shikimori.one", "shikimori.net"));
        Assert.Null(resolver.ActiveForkHost);

        Assert.True(resolver.Remember("shikimori.net", "shikimori.rip"));
        Assert.Equal("shikimori.rip", resolver.ActiveForkHost);
    }

    [Fact]
    public void Rewrite_ReplacesForkHostWithPinnedHostButLeavesOriginalRealmAlone()
    {
        var resolver = new ShikiHostResolver();
        resolver.Remember("shikimori.net", "shikimori.rip");

        var rewritten = resolver.Rewrite(new Uri("https://shikimori.net/api/animes/1"));
        var original = resolver.Rewrite(new Uri("https://shikimori.one/api/animes/1"));

        Assert.Equal("https://shikimori.rip/api/animes/1", rewritten.ToString());
        Assert.Equal("https://shikimori.one/api/animes/1", original.ToString());
    }

    [Fact]
    public void ForkProbeOrder_SkipsTheFailedHost()
    {
        var order = ShikiHostResolver.ForkProbeOrder("shikimori.net").ToArray();

        Assert.DoesNotContain("shikimori.net", order);
        Assert.Contains("shikimori.rip", order);
        Assert.Contains("shikimori.fi", order);
    }
}
