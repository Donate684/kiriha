using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.ViewModels;
using Serilog;

namespace Kiriha.Views;

public partial class PlayerOverlayWindow : Window
{
    private PlayerWindow _ownerWindow;
    private MpvPlayer? _player => _ownerWindow.Player;

    // Auto-hide: hide panels after 3 seconds of no mouse movement
    private static readonly TimeSpan ControlsKeepAliveInterval = TimeSpan.FromMilliseconds(180);
    private readonly DispatcherTimer _hideTimer;
    private bool _controlsVisible = true;
    private DateTime _lastControlsKeepAliveUtc = DateTime.MinValue;

    // Chapter markers
    private Border? _topBar;
    private Border? _bottomBar;
    private Border? _settingsOverlayBackdrop;
    private Slider? _timelineSlider;
    private Canvas? _chapterCanvas;
    private Button? _settingsButton;
    private Button? _screenshotButton;
    private Button? _closeButton;
    private TextBlock? _maximizeIcon;
    private PlayerViewModel? _subscribedViewModel;
    private PropertyChangedEventHandler? _viewModelPropertyChanged;
    private EventHandler<PixelPointEventArgs>? _ownerPositionChanged;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _ownerPropertyChanged;
    private DateTime _lastTimelinePreviewAt = DateTime.MinValue;
    private double _lastTimelinePreviewTime = -1;

    public PlayerOverlayWindow()
    {
        InitializeComponent();
        CacheOverlayControls();
        DisableLegacySettingsFlyout();
        _ownerWindow = null!;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += OnHideTimerTick;
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);
    }

    public PlayerOverlayWindow(PlayerWindow owner)
    {
        InitializeComponent();
        CacheOverlayControls();
        DisableLegacySettingsFlyout();
        _ownerWindow = owner;
        DataContext = owner.DataContext;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += OnHideTimerTick;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);

        if (_timelineSlider != null)
        {
            _timelineSlider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            _timelineSlider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
        }

        if (_screenshotButton != null)
            _screenshotButton.AddHandler(PointerReleasedEvent, OnScreenshotButtonPointerReleased, RoutingStrategies.Tunnel);

        // Subscribe to chapter changes from the ViewModel
        if (DataContext is PlayerViewModel vm)
        {
            _subscribedViewModel = vm;
            _viewModelPropertyChanged = OnViewModelPropertyChanged;
            vm.Chapters.CollectionChanged += OnChaptersChanged;
            vm.PropertyChanged += _viewModelPropertyChanged;
        }

        // Keep overlay synced with owner window
        _ownerPositionChanged = (_, _) => UpdateOverlayPosition();
        _ownerPropertyChanged = OnOwnerPropertyChanged;
        _ownerWindow.PositionChanged += _ownerPositionChanged;
        _ownerWindow.PropertyChanged += _ownerPropertyChanged;

        // Start with controls visible, then auto-hide
        _hideTimer.Start();
    }

    private void CacheOverlayControls()
    {
        _topBar = this.FindControl<Border>("TopBar");
        _bottomBar = this.FindControl<Border>("BottomBar");
        _settingsOverlayBackdrop = this.FindControl<Border>("SettingsOverlayBackdrop");
        _timelineSlider = this.FindControl<Slider>("TimelineSlider");
        _chapterCanvas = this.FindControl<Canvas>("ChapterCanvas");
        _settingsButton = this.FindControl<Button>("SettingsButton");
        _screenshotButton = this.FindControl<Button>("ScreenshotButton");
        _closeButton = this.FindControl<Button>("CloseButton");
        _maximizeIcon = this.FindControl<TextBlock>("MaximizeIcon");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files     = e.DataTransfer.TryGetFiles();
        var firstFile = files?.FirstOrDefault();
        if (firstFile != null && _player != null)
        {
            var path = firstFile.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path))
            {
                Log.Information("Dropped file: {Path}", path);
                if (DataContext is PlayerViewModel vm)
                {
                    vm.AnimeTitle   = System.IO.Path.GetFileName(path);
                    vm.EpisodeTitle = "Локальный файл";
                    vm.VideoUrl     = path;
                    vm.IsPlaying    = vm.PlayerAutoPlay;
                }
                if (DataContext is PlayerViewModel { PlayerAutoPlay: false })
                    _player.Pause();
                _player.Load(path);
                if (DataContext is PlayerViewModel { PlayerAutoPlay: true })
                    _player.Play();
            }
        }
    }
}
