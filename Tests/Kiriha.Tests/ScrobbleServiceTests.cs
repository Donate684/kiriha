using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services;
using Kiriha.Services.AppLifecycle;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Moq;

namespace Kiriha.Tests;

public class ScrobbleServiceTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly SettingsService _settingsService;
    private readonly Mock<AnimeProgressService> _mockProgressService;
    private readonly Mock<HistoryService> _mockHistoryService;
    private readonly Mock<NotificationService> _mockNotificationService;
    private readonly Mock<IBackgroundTaskSupervisor> _mockBackgroundTasks;
    private readonly ScrobbleService _scrobbleService;

    public ScrobbleServiceTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _settingsService = new SettingsService(_tempSettingsPath);

        // Setup initial settings
        _settingsService.Update(s =>
        {
            s.System.Scrobbler.Enabled = true;
            s.System.Scrobbler.DelaySeconds = 0; // immediate for testing
            s.System.Scrobbler.NotifyOnSkippedEpisode = true;
        }, save: false);

        _mockProgressService = new Mock<AnimeProgressService>(null!, null!, null!, null!);
        _mockHistoryService = new Mock<HistoryService>(null!);
        _mockNotificationService = new Mock<NotificationService>(null!, null!);
        _mockBackgroundTasks = new Mock<IBackgroundTaskSupervisor>();

        // When background task is queued, run it synchronously for testing
        _mockBackgroundTasks
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                task(ct).GetAwaiter().GetResult();
            });

        _scrobbleService = new ScrobbleService(
            _mockProgressService.Object,
            _mockHistoryService.Object,
            _settingsService,
            _mockNotificationService.Object,
            _mockBackgroundTasks.Object);
    }

    [Fact]
    public void StartScrobble_AlreadyScrobbled_DoesNothing()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "5" };
        var match = new AnimeItem { Progress = 5 };

        bool statusUpdated = false;
        _scrobbleService.CountdownUpdated += (s, e) => statusUpdated = true;

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        Assert.True(statusUpdated);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeItem>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);
        _mockBackgroundTasks.Verify(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StartScrobble_SkippedEpisode_NotifiesAndDoesNothing()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "7" };
        var match = new AnimeItem { Progress = 5 };

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        _mockNotificationService.Verify(x => x.NotifyScrobbleSkipped(match, 7), Times.Once);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(It.IsAny<AnimeItem>(), It.IsAny<int>(), It.IsAny<UserAnimeStatus?>()), Times.Never);
        _mockBackgroundTasks.Verify(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void StartScrobble_ValidEpisode_UpdatesProgress()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "6", IsPlaying = true };
        var match = new AnimeItem { Progress = 5, Id = 1, Title = "Test Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 6, It.IsAny<UserAnimeStatus?>()))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        _mockBackgroundTasks.Verify(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 6, It.IsAny<UserAnimeStatus?>()), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntry(match.Id, match.Title, match.RussianTitle, 6, "Scrobbled", null), Times.Once);
    }

    [Fact]
    public void StartScrobble_CompletesAnime_SetsStatusToCompleted()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "12", IsPlaying = true };
        var match = new AnimeItem { Progress = 11, TotalEpisodes = 12, Id = 1, Title = "Test Anime" };

        _mockProgressService
            .Setup(x => x.UpdateProgressAsync(match, 12, UserAnimeStatus.Completed))
            .ReturnsAsync(true);

        // Act
        _scrobbleService.StartScrobble(media, match);

        // Assert
        _mockProgressService.Verify(x => x.UpdateProgressAsync(match, 12, UserAnimeStatus.Completed), Times.Once);
        _mockHistoryService.Verify(x => x.AddEntry(match.Id, match.Title, match.RussianTitle, 12, "Completed", null), Times.Once);
    }

    [Fact]
    public void CancelScrobble_CancelsInProgressScrobble()
    {
        // Arrange
        var media = new ParsedMedia { Episode = "6", IsPlaying = true };
        var match = new AnimeItem { Progress = 5 };

        // Make the background task not complete immediately so we can cancel it
        _settingsService.Update(s => s.System.Scrobbler.DelaySeconds = 10, save: false);

        CancellationToken? taskToken = null;
        _mockBackgroundTasks
            .Setup(x => x.Run(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task>, CancellationToken>((name, task, ct) =>
            {
                taskToken = ct;
            });

        _scrobbleService.StartScrobble(media, match);

        Assert.NotNull(taskToken);
        Assert.False(taskToken.Value.IsCancellationRequested);

        // Act
        _scrobbleService.CancelScrobble();

        // Assert
        Assert.True(taskToken.Value.IsCancellationRequested);
    }

    public void Dispose()
    {
        _scrobbleService.Dispose();
        _settingsService.Dispose();
        if (File.Exists(_tempSettingsPath))
        {
            try { File.Delete(_tempSettingsPath); } catch { }
        }
    }
}
