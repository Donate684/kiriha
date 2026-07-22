using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Tracking;

public partial class TrackingService : IDisposable
{
    private readonly AnisthesiaService _anisthesiaService;
    private readonly MappingService _mappingService;
    private readonly AnimeRepository _animeRepo;
    private readonly SettingsService _settingsService;
    private readonly DiscordService _discordService;
    private readonly IScrobbleService _scrobbleService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IEnumerable<ITrackerService> _trackers;

    // _state guards _currentMedia and _matchedAnime which are read/written from the
    // Anisthesia background thread (MediaDetected/MediaCleared) and from UI command handlers.
    private readonly object _state = new();
    private ParsedMedia? _currentMedia;
    private AnimeItem? _matchedAnime;
    private bool _manualMapInProgress;

    public ParsedMedia? CurrentMedia { get { lock (_state) return _currentMedia; } }
    public AnimeItem? MatchedAnime { get { lock (_state) return _matchedAnime; } }

    public TrackingService(
        AnisthesiaService anisthesiaService,
        MappingService mappingService,
        AnimeRepository animeRepo,
        SettingsService settingsService,
        DiscordService discordService,
        IScrobbleService scrobbleService,
        IUiDispatcher uiDispatcher,
        IEnumerable<ITrackerService> trackers)
    {
        _anisthesiaService = anisthesiaService;
        _mappingService = mappingService;
        _animeRepo = animeRepo;
        _settingsService = settingsService;
        _discordService = discordService;
        _scrobbleService = scrobbleService;
        _uiDispatcher = uiDispatcher;
        _trackers = trackers;

        _anisthesiaService.MediaDetected += OnMediaDetected;
        _anisthesiaService.MediaCleared += OnMediaCleared;
        _scrobbleService.CountdownUpdated += OnScrobbleCountdownUpdated;
    }

    public void Dispose()
    {
        _anisthesiaService.MediaDetected -= OnMediaDetected;
        _anisthesiaService.MediaCleared -= OnMediaCleared;
        _scrobbleService.CountdownUpdated -= OnScrobbleCountdownUpdated;
        _scrobbleService.CancelScrobble();
    }
}
