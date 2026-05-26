using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Microsoft.EntityFrameworkCore;

namespace Kiriha.Tests;

public sealed class AppDbContextTests
{
    [Fact]
    public async Task UserAnime_RoundTripsStatusAndJsonCollectionsThroughSqlite()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        try
        {
            await using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.UserAnime.Add(new AnimeItem
                {
                    Id = 42,
                    Title = "Test Title",
                    Status = UserAnimeStatus.PlanToWatch,
                    Genres = new List<string> { "Drama", "Fantasy" },
                    Studios = new List<string> { "Studio A" },
                    AlternativeTitles = new List<string> { "Alt A", "Alt B" }
                });
                await context.SaveChangesAsync();
            }

            await using (var context = new AppDbContext(options))
            {
                var item = await context.UserAnime.SingleAsync(x => x.Id == 42);

                Assert.Equal(UserAnimeStatus.PlanToWatch, item.Status);
                Assert.Equal(new[] { "Drama", "Fantasy" }, item.Genres);
                Assert.Equal(new[] { "Studio A" }, item.Studios);
                Assert.Equal(new[] { "Alt A", "Alt B" }, item.AlternativeTitles);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
