using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.Services;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels;

public partial class PlayerViewModel : ObservableObject, IDisposable
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".ts", ".m2ts", ".mpg", ".mpeg", ".ogm", ".ogg"
    };

    private readonly IPlayerMediaMetadataResolver? _metadataResolver;
    private readonly SettingsService? _settingsService;
    private readonly PlayerPlaybackController _playback = new();
    private readonly PlayerStatePublisher _statePublisher;
    private readonly PlayerTimelineService _timeline = new();
    private readonly PlayerSettingsApplier _settingsApplier;
    private readonly PlayerTimelinePreviewController _timelinePreview;
    private DispatcherTimer? _timer;
    private bool _isApplyingSettings;
    private bool _mpvRuntimeDiagnosticsVisible;

    public PlayerOverlayViewModel Overlay { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> AudioTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<TrackInfo> SubtitleTracks { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ChapterInfo> Chapters { get; } = new();
    public List<PlayerMouseActionOption> MouseActionOptions { get; } = new()
    {
        new("Ничего", PlayerMouseAction.None),
        new("Пауза / воспроизведение", PlayerMouseAction.TogglePlayPause),
        new("Полноэкранный режим", PlayerMouseAction.ToggleFullscreen),
        new("Показать панель", PlayerMouseAction.ShowControls),
        new("Открыть настройки", PlayerMouseAction.OpenSettings),
        new("Назад на 10 секунд", PlayerMouseAction.SeekBackward10),
        new("Вперёд на 10 секунд", PlayerMouseAction.SeekForward10),
        new("Следующая аудиодорожка", PlayerMouseAction.CycleAudio),
        new("Следующие субтитры", PlayerMouseAction.CycleSubtitle)
    };
    public List<PlayerWheelActionOption> WheelActionOptions { get; } = new()
    {
        new("Ничего", PlayerWheelAction.None),
        new("Громче", PlayerWheelAction.VolumeUp),
        new("Тише", PlayerWheelAction.VolumeDown),
        new("Вперёд", PlayerWheelAction.SeekForward),
        new("Назад", PlayerWheelAction.SeekBackward),
        new("Скорость выше", PlayerWheelAction.SpeedUp),
        new("Скорость ниже", PlayerWheelAction.SpeedDown)
    };
    public List<int> WheelStepOptions { get; } = new() { 1, 2, 5, 10 };
    public List<string> ScreenshotFormatOptions { get; } = new() { "png", "jpg", "webp" };
    public List<ScreenshotResolutionOption> ScreenshotResolutionOptions { get; } = new()
    {
        new("Исходное видео", "video"),
        new("Размер окна", "window")
    };

    [ObservableProperty] private string _videoUrl = string.Empty;
    [ObservableProperty] private string _animeTitle = string.Empty;
    [ObservableProperty] private string _animeTitleRu = string.Empty;
    [ObservableProperty] private string _animeTitleEn = string.Empty;
    [ObservableProperty] private string _episodeTitle = string.Empty;
    [ObservableProperty] private string _rawEpisodeText = string.Empty;
    
    [ObservableProperty] private bool _isPlaying = true;
    [ObservableProperty] private double _currentTime = 0;
    [ObservableProperty] private double _duration = 0;
    [ObservableProperty] private string _currentTimeString = "00:00";
    [ObservableProperty] private string _durationString = "--:--";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasPlaybackError;
    [ObservableProperty] private string _playbackStatusMessage = "Загрузка видео...";
    [ObservableProperty] private string _playbackErrorMessage = string.Empty;
    [ObservableProperty] private bool _canOpenPreviousMedia;
    [ObservableProperty] private bool _canOpenNextMedia;
    
    [ObservableProperty] private double _volume = 100;
    [ObservableProperty] private bool _isMuted = false;
    [ObservableProperty] private bool _normalizeAudio = false;
    private double _previousVolume = 100;

    [ObservableProperty] private double _playbackSpeed = 1.0;

    [ObservableProperty] private bool _playerAutoPlay = true;
    [ObservableProperty] private bool _singlePlayerWindow = true;
    [ObservableProperty] private bool _rememberPlayerVolume = true;
    [ObservableProperty] private bool _autoHideControls = true;
    [ObservableProperty] private bool _showChapterMarkers = true;
    [ObservableProperty] private PlayerMouseActionOption? _leftClickAction;
    [ObservableProperty] private PlayerMouseActionOption? _rightClickAction;
    [ObservableProperty] private PlayerMouseActionOption? _middleClickAction;
    [ObservableProperty] private PlayerWheelActionOption? _wheelUpAction;
    [ObservableProperty] private PlayerWheelActionOption? _wheelDownAction;
    [ObservableProperty] private int _wheelVolumeStep = 5;
    [ObservableProperty] private bool _showPlayPauseButton = true;
    [ObservableProperty] private bool _showSkipButtons = true;
    [ObservableProperty] private bool _showMuteButton = true;
    [ObservableProperty] private bool _showVolumeSlider = true;
    [ObservableProperty] private bool _showTimeDisplay = true;
    [ObservableProperty] private bool _showSpeedButton = true;
    [ObservableProperty] private bool _showSubtitleButton = true;
    [ObservableProperty] private bool _showSubtitlePositionButton = true;
    [ObservableProperty] private bool _showAudioButton = true;
    [ObservableProperty] private bool _showScreenshotButton = true;
    [ObservableProperty] private bool _showSubtitleStyleButton = true;
    [ObservableProperty] private string _preferredAudioLanguages = "Japanese,jpn,ja";
    [ObservableProperty] private string _preferredSubtitleLanguages = "Russian,rus,ru";
    [ObservableProperty] private bool _subtitleStyleOverrideEnabled = false;
    [ObservableProperty] private string _subtitleStyleHotkey = "U";
    [ObservableProperty] private string _subtitleFont = "Candara Bold";
    [ObservableProperty] private double _subtitleFontSize = 60;
    [ObservableProperty] private string _subtitleColor = "#FFFFFF";
    [ObservableProperty] private string _subtitleBorderColor = "#000000";
    [ObservableProperty] private string _subtitleShadowColor = "#000000";
    [ObservableProperty] private double _subtitleBorderSize = 3.8;
    [ObservableProperty] private double _subtitleShadowOffset = 1.5;
    [ObservableProperty] private string _subtitleAlignY = "bottom";
    [ObservableProperty] private string _subtitleAlignX = "center";
    [ObservableProperty] private int _subtitleMarginY = 35;
    [ObservableProperty] private bool _subtitleScaleByWindow = true;
    [ObservableProperty] private string _screenshotDirectory = string.Empty;
    [ObservableProperty] private string _screenshotFormat = "png";
    [ObservableProperty] private ScreenshotResolutionOption? _screenshotResolution;
    [ObservableProperty] private int _screenshotPngCompression = 4;
    [ObservableProperty] private int _screenshotQuality = 95;
    [ObservableProperty] private bool _screenshotHighBitDepth = false;
    [ObservableProperty] private string _screenshotWithSubtitlesHotkey = "S";
    [ObservableProperty] private string _screenshotWithoutSubtitlesHotkey = "Shift+S";
    [ObservableProperty] private string _togglePlayPauseHotkey = "Space";
    [ObservableProperty] private string _toggleFullscreenHotkey = "F";
    [ObservableProperty] private string _exitFullscreenHotkey = "Escape";
    [ObservableProperty] private string _toggleMuteHotkey = "M";
    [ObservableProperty] private string _cycleAudioHotkey = "A";
    [ObservableProperty] private string _cycleSubtitleHotkey = "V";
    [ObservableProperty] private string _previousMediaHotkey = "P";
    [ObservableProperty] private string _nextMediaHotkey = "N";
    [ObservableProperty] private string _speedDownHotkey = "OemOpenBrackets";
    [ObservableProperty] private string _speedUpHotkey = "OemCloseBrackets";
    [ObservableProperty] private string _volumeUpHotkey = "Up";
    [ObservableProperty] private string _volumeDownHotkey = "Down";
    [ObservableProperty] private string _seekBackwardHotkey = "Left";
    [ObservableProperty] private string _seekForwardHotkey = "Right";
    [ObservableProperty] private string _reloadSubtitlesHotkey = "Q";
    [ObservableProperty] private string _frameStepForwardHotkey = "OemPeriod";
    [ObservableProperty] private string _frameStepBackwardHotkey = "OemComma";
    [ObservableProperty] private string _mpvScale = "ewa_lanczossharp";
    [ObservableProperty] private string _mpvChromaScale = "ewa_lanczossharp";
    [ObservableProperty] private string _mpvDitherDepth = "auto";
    [ObservableProperty] private bool _mpvCorrectDownscaling = true;
    [ObservableProperty] private bool _mpvDeband = true;
    [ObservableProperty] private int _mpvDebandIterations = 3;
    [ObservableProperty] private int _mpvDebandThreshold = 30;
    [ObservableProperty] private string _mpvHwdec = "auto";
    [ObservableProperty] private string _mpvVideoOutput = "gpu-next";
    [ObservableProperty] private string _mpvGpuApi = "auto";
    [ObservableProperty] private string _mpvGpuContext = "auto";
    [ObservableProperty] private string _mpvRuntimeInfo = "hwdec: -, interop: -, vo: -, context: -, decoder: -";

    private int? _animeId;
    private bool _isInitializing;

    public PlayerViewModel(
        string videoUrl,
        PlayerMediaMetadata? metadata = null,
        IPlayerMediaMetadataResolver? metadataResolver = null,
        SettingsService? settingsService = null)
    {
        _isInitializing = true;
        _metadataResolver = metadataResolver;
        _settingsService = settingsService;
        _statePublisher = new PlayerStatePublisher(CreatePlayerState);
        _settingsApplier = new PlayerSettingsApplier(_playback);
        _timelinePreview = new PlayerTimelinePreviewController(Overlay);
        ApplyPlayerSettings();
        ApplyMetadata(metadata ?? metadataResolver?.Resolve(videoUrl) ?? PlayerMediaMetadata.FromVideoPath(videoUrl));
        
        VideoUrl = videoUrl; // Sets VideoUrl and triggers OnVideoUrlChanged if needed, but since it's constructor, we already set the fields above.
        _isInitializing = false;
    }



}

public record PlayerMouseActionOption(string Name, PlayerMouseAction Value);
public record PlayerWheelActionOption(string Name, PlayerWheelAction Value);
public record ScreenshotResolutionOption(string Name, string Value);
