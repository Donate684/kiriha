using System;
using Avalonia.Controls;
using Kiriha.Core.Mpv;
using Kiriha.Services.Data;
using Kiriha.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Kiriha.Views;

public partial class PlayerWindow : Window
{
    private MpvPlayer? _player;
    private PlayerOverlayWindow? _overlay;
    private PlayerLoadingPipeline? _loadingPipeline;

    public MpvPlayer? Player => _player;
    
    public PlayerWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            Log.Information("Initializing MPV player with libmpv render API");
            var playerSettings = App.Services.GetRequiredService<SettingsService>().Current.Player;
            var mpvOptions = new MpvOptions(
                playerSettings.MpvHwdec,
                playerSettings.MpvVideoOutput,
                playerSettings.MpvGpuApi,
                playerSettings.MpvGpuContext);

            _player = new MpvPlayer(mpvOptions);

            if (DataContext is PlayerViewModel vm)
            {
                _loadingPipeline = new PlayerLoadingPipeline(vm, VideoHost);
            }

            _overlay = new PlayerOverlayWindow(this);
            _overlay.Show(this);

            VideoHost.RenderContextReady += OnVideoRenderContextReady;
            if (_loadingPipeline != null)
            {
                _loadingPipeline.AttachPlayer(_player);
            }
            else
            {
                VideoHost.Player = _player;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MPV player");
        }
    }

    private void OnVideoRenderContextReady(object? sender, EventArgs e)
    {
        _loadingPipeline?.MarkRenderContextReady();
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        VideoHost.RequestNextFrameRendering();
    }

    protected override void OnClosed(EventArgs e)
    {
        VideoHost.RenderContextReady -= OnVideoRenderContextReady;

        _overlay?.Close();
        _overlay = null;
        _loadingPipeline = null;

        base.OnClosed(e);

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        _player?.Dispose();
        _player = null;

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == this)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            }
        }
    }
}
