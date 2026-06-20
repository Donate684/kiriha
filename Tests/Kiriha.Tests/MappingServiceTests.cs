using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Services.Data;
using Xunit;

namespace Kiriha.Tests;

public sealed class MappingServiceTests
{
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
        catch { }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesS2Correctly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!);

            var userList = new List<AnimeItem>
            {
                new AnimeItem { Id = 1, Title = "Sousou no Frieren" },
                new AnimeItem { Id = 2, Title = "Sousou no Frieren Season 2" }
            };

            var id = await service.GetIdFromTitleAsync("[Erai-raws] Sousou no Frieren 2nd Season - 01 [1080p].mkv", userList);
            Assert.Equal(2, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesOvaCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!);

            var userList = new List<AnimeItem>
            {
                new AnimeItem { Id = 1, Title = "KonoSuba" },
                new AnimeItem { Id = 2, Title = "KonoSuba OVA" }
            };

            var id = await service.GetIdFromTitleAsync("KonoSuba OVA 1.mkv", userList);
            Assert.Equal(2, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesOnaCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!);

            var userList = new List<AnimeItem>
            {
                new AnimeItem { Id = 1, Title = "Cyberpunk Edgerunners" },
                new AnimeItem { Id = 2, Title = "Cyberpunk Edgerunners ONA" }
            };

            var id = await service.GetIdFromTitleAsync("Cyberpunk Edgerunners ONA 01.mkv", userList);
            Assert.Equal(2, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_MatchesCyrillicCorrectly()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!);

            var userList = new List<AnimeItem>
            {
                new AnimeItem { Id = 1, Title = "Sousou no Frieren", RussianTitle = "Фрирен, провожающая в последний путь" }
            };

            var id = await service.GetIdFromTitleAsync("[Субтитры] Фрирен, провожающая в последний путь - 01.mkv", userList);
            Assert.Equal(1, id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public async Task GetIdFromTitleAsync_DoesNotFallbackToS1IfS2ExplicitlyRequested()
    {
        var path = CreateTempMappingPath();
        try
        {
            var manualMapping = new ManualMappingService(path);
            var service = new MappingService(null!, manualMapping, null!);

            var userList = new List<AnimeItem>
            {
                new AnimeItem { Id = 1, Title = "Sousou no Frieren" }
            };

            var id = await service.GetIdFromTitleAsync("[Erai-raws] Sousou no Frieren Season 2 - 01.mkv", userList);
            Assert.Null(id);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }
}
