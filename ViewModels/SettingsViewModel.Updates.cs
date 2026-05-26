using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Serilog;

namespace Kiriha.ViewModels;

public partial class SettingsViewModel
{
    [RelayCommand]
    private void OpenReleasesPage()
    {
        UIUtils.OpenUrl(Constants.Links.GitHubReleases);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (_updateService.IsChecking) return;
        
        IsCheckingUpdates = true;
        IsUpdateAvailable = false;
        IsUpdateDownloaded = false;
        UpdateProgress = 0;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var found = await _updateService.CheckForUpdatesAsync(cts.Token);
            IsUpdateAvailable = found;
            NewVersion = _updateService.NewVersion;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "CheckForUpdates command failed");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (!IsUpdateAvailable || IsUpdateDownloaded || _updateService.IsDownloading) return;
        
        UpdateProgress = 1; // Show progress bar immediately
        try
        {
            using var cts = new CancellationTokenSource();
            var success = await _updateService.DownloadAndInstallAsync(p => UpdateProgress = p, cts.Token);
            if (success)
            {
                IsUpdateDownloaded = true;
            }
            else
            {
                UpdateProgress = 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DownloadUpdate command failed");
            UpdateProgress = 0;
        }
    }

    [RelayCommand]
    private void RestartAndApply()
    {
        _updateService.RestartAndApply();
    }
}
