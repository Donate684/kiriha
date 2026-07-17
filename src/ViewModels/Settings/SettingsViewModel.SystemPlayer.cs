using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Views;
using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel
{
    partial void OnEnableMicaChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.EnableMica = value, SettingsSection.UI);

        // Apply to all open windows
        if (Application.Current is App app && app.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (window is MainWindow main)
                    main.ApplyMica();
                else if (window is AnimeDetailsWindow details)
                    details.ApplyMica();
                else if (window is PlayerSelectionWindow playerSelection)
                    playerSelection.ApplyMica();
            }
        }
    }

    [RelayCommand]
    private async Task ManagePlayers()
    {
        using var viewModel = new PlayerSelectionViewModel(_anisthesiaService, _settingsService);
        var window = new PlayerSelectionWindow(_settingsService)
        {
            DataContext = viewModel
        };

        var mainWindow = (App.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);
            OnPropertyChanged(nameof(EnabledPlayersCount));
        }
    }

    [RelayCommand]
    private void RegisterSystemPlayer()
    {
        _systemIntegrationService.Register();
        IsSystemPlayer = _systemIntegrationService.IsRegistered();
    }

    [RelayCommand]
    private void UnregisterSystemPlayer()
    {
        _systemIntegrationService.Unregister();
        IsSystemPlayer = _systemIntegrationService.IsRegistered();
    }

    public partial class PlayerSelectionItem : ObservableObject
    {
        public string Name { get; }
        public Kiriha.Services.Tracking.Anisthesia.PlayerType Type { get; }
        
        [ObservableProperty]
        private bool _isEnabled;

        public PlayerSelectionItem(string name, Kiriha.Services.Tracking.Anisthesia.PlayerType type, bool isEnabled)
        {
            Name = name;
            Type = type;
            _isEnabled = isEnabled;
        }
    }

    partial void OnAutoLaunchChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.AutoLaunch = value, SettingsSection.System);
        if (value) StartupService.EnableStartup(LaunchMinimized);
        else StartupService.DisableStartup();
    }

    partial void OnLaunchMinimizedChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.LaunchMinimized = value, SettingsSection.System);
        if (AutoLaunch) StartupService.EnableStartup(value);
    }


    public record LanguageOption(string Name, string Code);
}
