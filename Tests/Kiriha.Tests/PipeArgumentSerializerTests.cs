using Kiriha.Core.Player;

namespace Kiriha.Tests;

public sealed class PipeArgumentSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesArgumentsWithSpacesAndUrls()
    {
        var args = new[]
        {
            "--player",
            @"C:\Anime\Some Title - 12.mkv",
            "--original-title",
            "Sousou no Frieren",
            "--stream",
            "https://example.test/watch?a=1&b=two words"
        };

        var serialized = PipeArgumentSerializer.Serialize(args);
        var parsed = PipeArgumentSerializer.Deserialize(serialized);

        Assert.Equal(args, parsed);
    }

    [Fact]
    public void Deserialize_FallsBackToLegacyPipeSeparator()
    {
        var parsed = PipeArgumentSerializer.Deserialize(@"--player||C:\Anime\file.mkv||--episode||12");

        Assert.Equal(new[] { "--player", @"C:\Anime\file.mkv", "--episode", "12" }, parsed);
    }

    [Fact]
    public void Deserialize_InvalidJsonReturnsOriginalAsSingleArgument()
    {
        var parsed = PipeArgumentSerializer.Deserialize("plain-argument");

        Assert.Equal(new[] { "plain-argument" }, parsed);
    }
}
