using Kiriha.Models.Entities;

namespace Kiriha.Tests;

public sealed class StatusMapperTests
{
    [Theory]
    [InlineData(UserAnimeStatus.Watching, "Watching", "watching", "watching")]
    [InlineData(UserAnimeStatus.Completed, "Completed", "completed", "completed")]
    [InlineData(UserAnimeStatus.OnHold, "On Hold", "on_hold", "on_hold")]
    [InlineData(UserAnimeStatus.Dropped, "Dropped", "dropped", "dropped")]
    [InlineData(UserAnimeStatus.PlanToWatch, "Plan to Watch", "plan_to_watch", "planned")]
    public void StatusMappings_ProduceExpectedExternalValues(
        UserAnimeStatus status,
        string db,
        string mal,
        string shiki)
    {
        Assert.Equal(db, StatusMapper.ToDbString(status));
        Assert.Equal(mal, StatusMapper.ToMal(status));
        Assert.Equal(shiki, StatusMapper.ToShiki(status));
    }

    [Theory]
    [InlineData("Watching", UserAnimeStatus.Watching)]
    [InlineData("on_hold", UserAnimeStatus.OnHold)]
    [InlineData("planned", UserAnimeStatus.PlanToWatch)]
    [InlineData("", UserAnimeStatus.None)]
    [InlineData(null, UserAnimeStatus.None)]
    public void FromDbString_AcceptsLegacyAndApiSpellings(string? input, UserAnimeStatus expected)
    {
        Assert.Equal(expected, StatusMapper.FromDbString(input));
    }

    [Theory]
    [InlineData("watching", UserAnimeStatus.Watching)]
    [InlineData("completed", UserAnimeStatus.Completed)]
    [InlineData("on_hold", UserAnimeStatus.OnHold)]
    [InlineData("dropped", UserAnimeStatus.Dropped)]
    [InlineData("plan_to_watch", UserAnimeStatus.PlanToWatch)]
    [InlineData("unknown", UserAnimeStatus.None)]
    public void FromMal_MapsKnownStatuses(string input, UserAnimeStatus expected)
    {
        Assert.Equal(expected, StatusMapper.FromMal(input));
    }
}
