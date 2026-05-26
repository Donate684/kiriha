using Kiriha.Services.Data;

namespace Kiriha.Tests;

public sealed class ManualMappingServiceTests
{
    [Fact]
    public async Task AddMapping_NormalizesTitleAndPersistsToDisk()
    {
        var path = CreateTempMappingPath();
        try
        {
            var service = new ManualMappingService(path);

            service.AddMapping("Sousou no Frieren", 52991);
            await service.FlushAsync();

            var reloaded = new ManualMappingService(path);

            Assert.True(reloaded.TryGetMapping("sousou no frieren", out var malId));
            Assert.Equal(52991, malId);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task AddNegativeMapping_PreventsPositiveLookupAndSurvivesReload()
    {
        var path = CreateTempMappingPath();
        try
        {
            var service = new ManualMappingService(path);

            service.AddNegativeMapping("Wrong Window Title");
            await service.FlushAsync();

            var reloaded = new ManualMappingService(path);

            Assert.True(reloaded.IsNegativelyMapped("wrong window title"));
            Assert.False(reloaded.TryGetMapping("wrong window title", out _));
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task RemoveMapping_ReturnsTrueOnlyWhenEntryExisted()
    {
        var path = CreateTempMappingPath();
        try
        {
            var service = new ManualMappingService(path);
            service.AddMapping("Cowboy Bebop", 1);
            await service.FlushAsync();

            Assert.True(service.RemoveMapping("Cowboy Bebop"));
            Assert.False(service.RemoveMapping("Cowboy Bebop"));
            await service.FlushAsync();

            var reloaded = new ManualMappingService(path);
            Assert.False(reloaded.TryGetMapping("cowboy bebop", out _));
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void BlankTitlesAreIgnored()
    {
        var path = CreateTempMappingPath();
        try
        {
            var service = new ManualMappingService(path);

            service.AddMapping(" ", 1);
            service.AddNegativeMapping("");

            Assert.False(service.TryGetMapping("", out _));
            Assert.False(service.IsNegativelyMapped(""));
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    private static string CreateTempMappingPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "title_mappings.json");
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
        }
    }
}
