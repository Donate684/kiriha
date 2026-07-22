using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Services;

namespace Kiriha.ViewModels;

public partial class UpdateDialogViewModel : ViewModelBase
{
    private readonly UpdateService _updateService;
    private readonly Action _closeAction;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty]
    private string _versionText;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _progress;

    [ObservableProperty]
    private bool _isDownloaded;

    public bool IsDownloadingOrFinished => IsDownloading || IsDownloaded;

    public string ProgressText => $"{Progress}%";

    public string NewVersionLabel => _updateService.NewVersion is { } v ? $"v{v}" : string.Empty;

    public UpdateDialogViewModel(UpdateService updateService, Action closeAction, bool isDownloaded = false)
    {
        _updateService = updateService;
        _closeAction = closeAction;
        _isDownloaded = isDownloaded;

        if (isDownloaded)
        {
            _versionText = Core.UIUtils.GetLoc("updates.downloaded");
        }
        else
        {
            _versionText = Core.UIUtils.GetLoc("updates.found.message", _updateService.NewVersion);
        }
    }

    [RelayCommand]
    private async Task StartUpdateAsync()
    {
        if (IsDownloading || IsDownloaded) return;

        IsDownloading = true;
        Progress = 0;
        OnPropertyChanged(nameof(IsDownloadingOrFinished));

        try
        {
            var success = await _updateService.DownloadAndInstallAsync(p =>
            {
                Progress = p;
                OnPropertyChanged(nameof(ProgressText));
            }, _cts.Token);

            if (success)
            {
                IsDownloaded = true;
                IsDownloading = false;
                OnPropertyChanged(nameof(IsDownloadingOrFinished));
            }
            else if (!_cts.IsCancellationRequested)
            {
                // Handle failure if needed, or close dialog
                _closeAction();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when closing dialog
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to download and install update via dialog");
            if (!_cts.IsCancellationRequested) _closeAction();
        }
    }

    [RelayCommand]
    private void RestartApp()
    {
        _updateService.RestartAndApply();
    }

    [RelayCommand]
    private void CloseDialog()
    {
        _cts.Cancel();
        _closeAction();
    }
}
