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

    public MpvPlayer? Player => _player;
    
    public PlayerWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (this.TryGetPlatformHandle() is { } handle)
        {
            try
            {
                Log.Information("Initializing MPV player with HWND: {Handle}", handle.Handle);
                var playerSettings = App.Services.GetRequiredService<SettingsService>().Current.Player;
                var mpvOptions = new MpvOptions(
                    playerSettings.MpvHwdec,
                    playerSettings.MpvVideoOutput,
                    playerSettings.MpvGpuApi,
                    playerSettings.MpvGpuContext);
                _player = new MpvPlayer(handle.Handle, mpvOptions);

                if (DataContext is PlayerViewModel vm)
                {
                    vm.Initialize(_player);
                    Log.Information("Loading video: {Url}", vm.VideoUrl);
                    vm.LoadVideo(vm.VideoUrl);
                }

                // Show transparent overlay for UI
                _overlay = new PlayerOverlayWindow(this);
                _overlay.Show(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize MPV player");
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _overlay?.Close();
        _overlay = null;

        if (DataContext is IDisposable disposable)
            disposable.Dispose();

        _player?.Dispose();
        _player = null;

        base.OnClosed(e);

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow == this)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => desktop.Shutdown());
            }
        }
    }
}
