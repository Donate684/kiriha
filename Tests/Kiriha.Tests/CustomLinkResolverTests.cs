using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;

namespace Kiriha.Tests;

public sealed class CustomLinkResolverTests
{
    [Fact]
    public void Resolve_EncodesTextPlaceholdersAndKeepsNumericIdsRaw()
    {
        var anime = new AnimeItem
        {
            Id = 5114,
            Title = "Fullmetal Alchemist: Brotherhood",
            EnglishTitle = "Fullmetal Alchemist & Brotherhood",
            RussianTitle = "Стальной алхимик: Братство",
            JapaneseTitle = "鋼の錬金術師"
        };

        var resolved = CustomLinkResolver.Resolve(
            "https://example.test/search?q={english}&ru={russian}&jp={japanese}&id={id}&mal={malId}&shiki={shikiId}",
            anime);

        Assert.Equal(
            "https://example.test/search?q=Fullmetal%20Alchemist%20%26%20Brotherhood&ru=%D0%A1%D1%82%D0%B0%D0%BB%D1%8C%D0%BD%D0%BE%D0%B9%20%D0%B0%D0%BB%D1%85%D0%B8%D0%BC%D0%B8%D0%BA%3A%20%D0%91%D1%80%D0%B0%D1%82%D1%81%D1%82%D0%B2%D0%BE&jp=%E9%8B%BC%E3%81%AE%E9%8C%AC%E9%87%91%E8%A1%93%E5%B8%AB&id=5114&mal=5114&shiki=5114",
            resolved);
    }

    [Fact]
    public void Resolve_FallsBackToTitleWhenOptionalTitlesAreMissing()
    {
        var anime = new AnimeItem { Id = 1, Title = "Cowboy Bebop" };

        var resolved = CustomLinkResolver.Resolve("{english}|{russian}|{japanese}", anime);

        Assert.Equal("Cowboy%20Bebop|Cowboy%20Bebop|", resolved);
    }

    [Fact]
    public void Resolve_NullAnimeOrTemplateReturnsSafeString()
    {
        Assert.Equal(string.Empty, CustomLinkResolver.Resolve(null!, new AnimeItem()));
        Assert.Equal("https://example.test/{title}", CustomLinkResolver.Resolve("https://example.test/{title}", null));
    }
}
