using System;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.Player;

public sealed partial class PlayerOverlayViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _osdTimer = new() { Interval = TimeSpan.FromSeconds(1.4) };

    [ObservableProperty] private bool _isOsdVisible;
    [ObservableProperty] private string _osdMessage = string.Empty;
    [ObservableProperty] private string _osdDetail = string.Empty;
    [ObservableProperty] private bool _isTimelinePreviewVisible;
    [ObservableProperty] private Bitmap? _timelinePreviewImage;
    [ObservableProperty] private string _timelinePreviewTime = string.Empty;
    [ObservableProperty] private double _timelinePreviewLeft;

    public PlayerOverlayViewModel()
    {
        _osdTimer.Tick += OnOsdTimerTick;
    }

    public void ShowOsd(string message, string detail = "")
    {
        OsdMessage = message;
        OsdDetail = detail;
        IsOsdVisible = true;

        _osdTimer.Stop();
        _osdTimer.Start();
    }

    public void ShowTimelinePreview(double timeSeconds, double left)
    {
        TimelinePreviewLeft = Math.Max(0, left);
        TimelinePreviewTime = PlayerTimelineService.FormatTime(timeSeconds);
        IsTimelinePreviewVisible = true;
    }

    public void SetTimelinePreviewImage(Bitmap image)
    {
        TimelinePreviewImage?.Dispose();
        TimelinePreviewImage = image;
    }

    public void HideTimelinePreview()
    {
        IsTimelinePreviewVisible = false;
    }

    public void ClearTimelinePreview()
    {
        TimelinePreviewImage?.Dispose();
        TimelinePreviewImage = null;
        TimelinePreviewTime = string.Empty;
        TimelinePreviewLeft = 0;
        IsTimelinePreviewVisible = false;
    }

    private void OnOsdTimerTick(object? sender, EventArgs e)
    {
        _osdTimer.Stop();
        IsOsdVisible = false;
    }

    public void Dispose()
    {
        _osdTimer.Stop();
        _osdTimer.Tick -= OnOsdTimerTick;
        ClearTimelinePreview();
    }
}
