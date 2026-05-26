using Kiriha.Models;
using Kiriha.Models.Api;

namespace Kiriha.Tests;

public sealed class TokenTests
{
    [Fact]
    public void MalTokens_AreExpiredInsideSixtySecondRefreshWindow()
    {
        var freshEnough = new MalTokens
        {
            CreatedAt = DateTime.UtcNow.AddSeconds(-59),
            ExpiresIn = 120
        };

        var insideRefreshWindow = new MalTokens
        {
            CreatedAt = DateTime.UtcNow.AddSeconds(-61),
            ExpiresIn = 120
        };

        Assert.False(freshEnough.IsExpired);
        Assert.True(insideRefreshWindow.IsExpired);
    }

    [Fact]
    public void ShikiTokens_TrackMirrorAndExpiry()
    {
        var tokens = new ShikiTokens
        {
            Mirror = ShikiMirror.Net,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ExpiresIn = 120
        };

        Assert.Equal(ShikiMirror.Net, tokens.Mirror);
        Assert.True(tokens.IsExpired);
    }
}
