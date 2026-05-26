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
    private readonly DispatcherTimer _hideTimer;
    private bool _controlsVisible = true;

    // Chapter markers
    private Canvas? _chapterCanvas;
    private PlayerViewModel? _subscribedViewModel;
    private PropertyChangedEventHandler? _viewModelPropertyChanged;
    private EventHandler<PixelPointEventArgs>? _ownerPositionChanged;
    private EventHandler<AvaloniaPropertyChangedEventArgs>? _ownerPropertyChanged;
    private DateTime _lastTimelinePreviewAt = DateTime.MinValue;
    private double _lastTimelinePreviewTime = -1;

    public PlayerOverlayWindow()
    {
        InitializeComponent();
        DisableLegacySettingsFlyout();
        _ownerWindow = null!;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += OnHideTimerTick;
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);
    }

    public PlayerOverlayWindow(PlayerWindow owner)
    {
        InitializeComponent();
        DisableLegacySettingsFlyout();
        _ownerWindow = owner;
        DataContext = owner.DataContext;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += OnHideTimerTick;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnOverlayKeyDown, RoutingStrategies.Tunnel);

        var slider = this.FindControl<Slider>("TimelineSlider");
        if (slider != null)
        {
            slider.AddHandler(PointerPressedEvent, OnSliderPointerPressed, RoutingStrategies.Tunnel);
            slider.AddHandler(PointerReleasedEvent, OnSliderPointerReleased, RoutingStrategies.Tunnel);
        }

        if (this.FindControl<Button>("ScreenshotButton") is { } screenshotButton)
            screenshotButton.AddHandler(PointerReleasedEvent, OnScreenshotButtonPointerReleased, RoutingStrategies.Tunnel);

        _chapterCanvas = this.FindControl<Canvas>("ChapterCanvas");

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
