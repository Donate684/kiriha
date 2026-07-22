using Kiriha.Core.Infrastructure;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Moq;

namespace Kiriha.Tests;

public class TrackingServiceTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly string _tempMappingPath;
    private readonly SettingsService _settingsService;
    private readonly Mock<IScrobbleService> _mockScrobbleService;
    private readonly Mock<IUiDispatcher> _mockUiDispatcher;
    private readonly Mock<MappingService> _mockMappingService;
    private readonly Mock<DiscordService> _mockDiscordService;
    private readonly Mock<AnisthesiaService> _mockAnisthesiaService;
    private readonly AnimeRepository _animeRepository;
    private readonly TrackingService _trackingService;

    public TrackingServiceTests()
    {
        _tempSettingsPath = Path.GetTempFileName();
        _tempMappingPath = Path.GetTempFileName();

        _settingsService = new SettingsService(_tempSettingsPath);
        _settingsService.Update(s => s.System.Scrobbler.Enabled = true, save: false);

        _mockScrobbleService = new Mock<IScrobbleService>();

        _mockUiDispatcher = new Mock<IUiDispatcher>();
        _mockUiDispatcher.Setup(x => x.Post(It.IsAny<Action>())).Callback<Action>(a => a());
        _mockUiDispatcher.Setup(x => x.InvokeAsync(It.IsAny<Func<List<AnimeItem>>>()))
            .Returns<Func<List<AnimeItem>>>(f => Task.FromResult(f()));

        _mockMappingService = new Mock<MappingService>(null!, new ManualMappingService(_tempMappingPath), null!, new RecognitionCache());
        _mockDiscordService = new Mock<DiscordService>(_settingsService);
        _mockAnisthesiaService = new Mock<AnisthesiaService>(_settingsService, null!, null!);

        _animeRepository = new AnimeRepository(null!, null!, null!, _mockUiDispatcher.Object, null!);
        // Force the initialization task to complete so we don't wait 5 seconds in tests
        var tcsField = typeof(AnimeRepository).GetField("_initTcs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (tcsField?.GetValue(_animeRepository) is TaskCompletionSource tcs)
        {
            tcs.TrySetResult();
        }

        _trackingService = new TrackingService(
            _mockAnisthesiaService.Object,
            _mockMappingService.Object,
            _animeRepository,
            _settingsService,
            _mockDiscordService.Object,
            _mockScrobbleService.Object,
            _mockUiDispatcher.Object,
            Array.Empty<ITrackerService>()
        );
    }

    [Fact]
    public void SetInternalMedia_ValidMatch_StartsScrobble()
    {
        // Arrange
        var state = new InternalPlayerState
        {
            AnimeTitle = "Test Anime",
            OriginalTitle = "Test Anime [1080p].mkv",
            Episode = "1",
            IsPlaying = true,
            Position = 10,
            Duration = 1400
        };

        var match = new AnimeItem { Id = 123, Title = "Test Anime", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        _mockMappingService
            .Setup(x => x.GetIdFromTitleAsync(state.OriginalTitle, It.IsAny<IEnumerable<AnimeItem>>()))
            .ReturnsAsync(123);

        // Act
        _trackingService.SetInternalMedia(state);

        // Assert
        _mockScrobbleService.Verify(x => x.StartScrobble(It.Is<ParsedMedia>(m => m.AnimeTitle == "Test Anime"), match), Times.Once);
        _mockDiscordService.Verify(x => x.UpdatePresence(
            It.IsAny<string>(), state.Episode, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<TimeSpan?>(), It.IsAny<TimeSpan?>(), It.IsAny<string>(), true), Times.Once);
    }

    [Fact]
    public void SetInternalMedia_WithExplicitId_StartsScrobbleDirectly()
    {
        // Arrange
        var state = new InternalPlayerState
        {
            AnimeTitle = "Explicit Anime",
            Episode = "2",
            IsPlaying = true,
            AnimeId = 456
        };

        var match = new AnimeItem { Id = 456, Title = "Explicit Anime", Status = UserAnimeStatus.Watching };
        _animeRepository.Collection.Add(match);

        // Act
        _trackingService.SetInternalMedia(state);

        // Assert
        _mockScrobbleService.Verify(x => x.StartScrobble(It.Is<ParsedMedia>(m => m.AnimeTitle == "Explicit Anime"), match), Times.Once);
        _mockMappingService.Verify(x => x.GetIdFromTitleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<AnimeItem>>()), Times.Never);
    }

    [Fact]
    public void SetInternalMedia_NegativelyMapped_DoesNotMatch()
    {
        // Arrange
        var state = new InternalPlayerState
        {
            AnimeTitle = "Bad Anime",
            OriginalTitle = "Bad Anime",
            Episode = "1",
            IsPlaying = true
        };

        _mockMappingService.Setup(x => x.IsNegativelyMapped(state.OriginalTitle)).Returns(true);

        // Act
        _trackingService.SetInternalMedia(state);

        // Assert
        _mockMappingService.Verify(x => x.GetIdFromTitleAsync(It.IsAny<string>(), It.IsAny<IEnumerable<AnimeItem>>()), Times.Never);
        _mockScrobbleService.Verify(x => x.StartScrobble(It.IsAny<ParsedMedia>(), It.IsAny<AnimeItem>()), Times.Never);
    }

    public void Dispose()
    {
        _trackingService.Dispose();
        _settingsService.Dispose();
        if (File.Exists(_tempSettingsPath)) try { File.Delete(_tempSettingsPath); } catch { }
        if (File.Exists(_tempMappingPath)) try { File.Delete(_tempMappingPath); } catch { }
    }
}
