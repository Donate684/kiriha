using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;

namespace Kiriha.Tests;

public sealed class ShikiHostResolverTests
{
    [Theory]
    [InlineData("shikimori.one", true, false)]
    [InlineData("shikimori.io", true, false)]
    [InlineData("shikimori.net", false, true)]
    [InlineData("shikimori.rip", false, true)]
    [InlineData("shikimori.fi", false, true)]
    [InlineData("example.com", false, false)]
    public void HostClassification_DistinguishesOriginalAndForkRealms(
        string host,
        bool isOriginal,
        bool isFork)
    {
        var resolver = new ShikiHostResolver();
        Assert.Equal(isOriginal || isFork, ShikiHostResolver.IsShikiHost(host));
        Assert.Equal(isOriginal, resolver.IsOriginalHost(host));
        Assert.Equal(isFork, resolver.IsForkHost(host));
    }

    [Fact]
    public void Remember_PinsWithinSameRealm()
    {
        var resolver = new ShikiHostResolver();

        Assert.False(resolver.Remember("shikimori.one", "shikimori.net"));
        Assert.Null(resolver.ActiveForkHost);
        Assert.Null(resolver.ActiveOriginalHost);

        Assert.True(resolver.Remember("shikimori.net", "shikimori.rip"));
        Assert.Equal("shikimori.rip", resolver.ActiveForkHost);
        
        Assert.True(resolver.Remember("shikimori.one", "shikimori.io"));
        Assert.Equal("shikimori.io", resolver.ActiveOriginalHost);
    }

    [Fact]
    public void Rewrite_ReplacesHostWithPinnedHostForBothRealms()
    {
        var resolver = new ShikiHostResolver();
        resolver.Remember("shikimori.net", "shikimori.rip");
        resolver.Remember("shikimori.one", "shikimori.io");

        var rewrittenFork = resolver.Rewrite(new Uri("https://shikimori.net/api/animes/1"));
        var rewrittenOriginal = resolver.Rewrite(new Uri("https://shikimori.one/api/animes/1"));

        Assert.Equal("https://shikimori.rip/api/animes/1", rewrittenFork.ToString());
        Assert.Equal("https://shikimori.io/api/animes/1", rewrittenOriginal.ToString());
    }

    [Fact]
    public void ProbeOrder_SkipsTheFailedHost()
    {
        var resolver = new ShikiHostResolver();
        
        var forkOrder = resolver.ProbeOrder("shikimori.net").ToArray();
        Assert.DoesNotContain("shikimori.net", forkOrder);
        Assert.Contains("shikimori.rip", forkOrder);
        Assert.Contains("shikimori.fi", forkOrder);
        
        var originalOrder = resolver.ProbeOrder("shikimori.one").ToArray();
        Assert.DoesNotContain("shikimori.one", originalOrder);
        Assert.Contains("shikimori.io", originalOrder);
    }
}
