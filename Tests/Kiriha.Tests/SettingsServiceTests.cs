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
                playerProcess.Update(settings => settings.Player.Volume = 33, Kiriha.Services.Data.SettingsSection.Player, save: false);
                playerProcess.SaveImmediate();

                mainProcess.Update(settings => settings.UI.LanguageCode = "ru", Kiriha.Services.Data.SettingsSection.UI, save: false);
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
    public void Update_WithExplicitSection_MergesWithoutJsonDiff()
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
                playerProcess.Update(settings => settings.Player.Volume = 33, SettingsSection.Player, save: false);
                playerProcess.SaveImmediate();

                mainProcess.Update(settings => settings.UI.LanguageCode = "ru", SettingsSection.UI, save: false);
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
    public void Load_ReadsSettingsWhileAnotherProcessHasWriteAccess()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var initial = new SettingsService(path))
            {
                initial.Update(settings =>
                {
                    settings.UI.LanguageCode = "ru";
                    settings.Player.Volume = 55;
                }, save: false);
                initial.SaveImmediate();
            }

            using var lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            using var service = new SettingsService(path);

            Assert.Equal("ru", service.Current.UI.LanguageCode);
            Assert.Equal(55, service.Current.Player.Volume);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void Dispose_DoesNotOverwriteSettingsWhenLoadFailedBecauseFileIsLocked()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var initial = new SettingsService(path))
            {
                initial.Update(settings => settings.UI.LanguageCode = "ru", save: false);
                initial.SaveImmediate();
            }

            using (var lockHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (new SettingsService(path))
            {
            }

            using var reloaded = new SettingsService(path);

            Assert.Equal("ru", reloaded.Current.UI.LanguageCode);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void SaveImmediate_CreatesBackupOfPreviousSettings()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var service = new SettingsService(path))
            {
                service.Update(settings => settings.UI.LanguageCode = "ru", save: false);
                service.SaveImmediate();

                service.Update(settings => settings.UI.LanguageCode = "uk", save: false);
                service.SaveImmediate();
            }

            using var current = new SettingsService(path);
            using var backup = new SettingsService(path + ".bak");

            Assert.Equal("uk", current.Current.UI.LanguageCode);
            Assert.Equal("ru", backup.Current.UI.LanguageCode);
        }
        finally
        {
            DeleteQuietly(path);
        }
    }

    [Fact]
    public void Load_RestoresSettingsFromBackupWhenPrimaryJsonIsCorrupted()
    {
        var path = CreateTempSettingsPath();
        try
        {
            using (var service = new SettingsService(path))
            {
                service.Update(settings => settings.UI.LanguageCode = "ru", save: false);
                service.SaveImmediate();

                service.Update(settings => settings.UI.LanguageCode = "uk", save: false);
                service.SaveImmediate();
            }
            File.WriteAllText(path, "broken json");

            using (new SettingsService(path))
            {
            }

            using var restored = new SettingsService(path);
            using var backup = new SettingsService(path + ".bak");

            Assert.Equal("ru", restored.Current.UI.LanguageCode);
            Assert.Equal("ru", backup.Current.UI.LanguageCode);
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
