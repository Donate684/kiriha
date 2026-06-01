using Kiriha.Models.Api;
using Kiriha.Services.Data;

namespace Kiriha.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void Constructor_CreatesSettingsFileWhenMissing()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using var service = new SettingsService(path);

            Assert.True(File.Exists(path));
            Assert.Equal("en", service.Current.UI.LanguageCode);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void SaveImmediate_AndReload_PreserveRegularSettings()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var service = new SettingsService(path))
            {
                service.Update(settings =>
                {
                    settings.UI.LanguageCode = "ru";
                    settings.Player.Volume = 42;
                    settings.System.CompletedSetupSteps.Add("language");
                }, save: false);
                service.SaveImmediate();
            }

            using var reloaded = new SettingsService(path);

            Assert.Equal("ru", reloaded.Current.UI.LanguageCode);
            Assert.Equal(42, reloaded.Current.Player.Volume);
            Assert.Contains("language", reloaded.Current.System.CompletedSetupSteps);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void Update_CentralizesMutationAndSchedulesSave()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var service = new SettingsService(path))
            {
                service.Update(settings =>
                {
                    settings.UI.LanguageCode = "ru";
                    settings.Player.Volume = 25;
                }, save: false);
                service.SaveImmediate();
            }

            using var reloaded = new SettingsService(path);

            Assert.Equal("ru", reloaded.Current.UI.LanguageCode);
            Assert.Equal(25, reloaded.Current.Player.Volume);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void SaveImmediate_MergesSectionsChangedBySeparateServices()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var initial = new SettingsService(path))
            {
                initial.Update(settings =>
                {
                    settings.UI.LanguageCode = "en";
                    settings.Player.Volume = 100;
                }, save: false);
                initial.SaveImmediate();
            }

            using (var mainProcess = new SettingsService(path))
            using (var playerProcess = new SettingsService(path))
            {
                playerProcess.Update(settings => settings.Player.Volume = 33, save: false);
                playerProcess.SaveImmediate();

                mainProcess.Update(settings => settings.UI.LanguageCode = "ru", save: false);
                mainProcess.SaveImmediate();
            }

            using var reloaded = new SettingsService(path);

            Assert.Equal("ru", reloaded.Current.UI.LanguageCode);
            Assert.Equal(33, reloaded.Current.Player.Volume);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void Load_CorruptedJsonFallsBackToDefaults()
    {
        var path = CreateTempSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ broken json");

            using var service = new SettingsService(path);

            Assert.Equal("en", service.Current.UI.LanguageCode);
            Assert.Null(service.Current.Api.Mal);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void Dispose_RewritesCorruptedJsonWithDefaults()
    {
        var path = CreateTempSettingsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ broken json");

            using (new SettingsService(path))
            {
            }

            using var reloaded = new SettingsService(path);

            Assert.Equal("en", reloaded.Current.UI.LanguageCode);
            Assert.Null(reloaded.Current.Api.Mal);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void SaveImmediate_EncryptsOAuthTokensAtRestAndReloadsThem()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var service = new SettingsService(path))
            {
                service.Update(settings => settings.Api.Mal = new MalTokens
                {
                    AccessToken = "mal-access-token",
                    RefreshToken = "mal-refresh-token",
                    ExpiresIn = 3600
                }, save: false);
                service.SaveImmediate();
            }

            var rawJson = File.ReadAllText(path);
            Assert.DoesNotContain("mal-access-token", rawJson);
            Assert.DoesNotContain("mal-refresh-token", rawJson);

            using var reloaded = new SettingsService(path);
            Assert.Equal("mal-access-token", reloaded.Current.Api.Mal?.AccessToken);
            Assert.Equal("mal-refresh-token", reloaded.Current.Api.Mal?.RefreshToken);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    private static string CreateTempSettingsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Kiriha.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
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
