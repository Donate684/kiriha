using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
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
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel
{
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (Application.Current == null || value == null) return;
        _settingsService.Update(settings => settings.UI.Theme = value.Value, SettingsSection.UI);

        Application.Current.RequestedThemeVariant = value.Value switch
        {
            ThemeType.Light => ThemeVariant.Light,
            ThemeType.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }



    partial void OnUseRussianTitlesChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseRussianTitles = value, SettingsSection.UI);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnUseRussianDescriptionsChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseRussianDescriptions = value, SettingsSection.UI);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnUiScaleChanged(double value)
    {
        _settingsService.Update(settings => settings.UI.UiScale = value, SettingsSection.UI);
        
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (window is Views.KirihaWindowBase kb)
                {
                    kb.ApplyUiScale(value);
                }
            }
        }
    }

    partial void OnShowAiringInfoChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ShowAiringInfo = value, SettingsSection.UI);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.CloseToTray = value, SettingsSection.System);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.MinimizeToTray = value, SettingsSection.System);
    }

    partial void OnEnableBackgroundMetadataFetchChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.EnableBackgroundMetadataFetch = value, SettingsSection.System);
    }

    partial void OnEnableLoggingChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.EnableLogging = value, SettingsSection.System);
    }

    partial void OnKeepPlayerProcessAliveChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.KeepPlayerProcessAlive = value, SettingsSection.System);

        if (value) PlayerProcessBridge.StartResident();
        else _ = PlayerProcessBridge.StopResidentAsync();
    }

    partial void OnSinglePlayerWindowChanged(bool value)
    {
        _settingsService.Update(settings => settings.Player.SingleWindow = value, SettingsSection.Player);
    }

    partial void OnMpvHwdecChanged(string value) =>
        SaveMpvOption(x => x.MpvHwdec = NormalizeMpvOption(value, "auto"));

    partial void OnMpvVideoOutputChanged(string value) =>
        SaveMpvOption(x => x.MpvVideoOutput = NormalizeMpvOption(value, "gpu-next"));

    partial void OnMpvGpuApiChanged(string value) =>
        SaveMpvOption(x => x.MpvGpuApi = NormalizeMpvOption(value, "auto"));

    partial void OnMpvGpuContextChanged(string value) =>
        SaveMpvOption(x => x.MpvGpuContext = NormalizeMpvOption(value, "auto"));

    private void SaveMpvOption(Action<AppSettings.PlayerConfig> update)
    {
        _settingsService.Update(settings => update(settings.Player), SettingsSection.Player);
    }

    private static string NormalizeMpvOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    partial void OnEnableScrobblerChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.Scrobbler.Enabled = value, SettingsSection.System);
    }

    partial void OnScrobbleDelaySecondsChanged(decimal? value)
    {
        if (value.HasValue)
        {
            _settingsService.Update(settings => settings.System.Scrobbler.DelaySeconds = (int)value.Value, SettingsSection.System);
        }
    }

    partial void OnScrobbleNotifyOnSkipChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.Scrobbler.NotifyOnSkippedEpisode = value, SettingsSection.System);
    }

    partial void OnEnableDiscordRPCChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.EnableDiscordRPC = value, SettingsSection.System);
        // Update Discord service status
        _discordService.UpdateStatus(value);
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value != null && value.Code != _settingsService.Read(settings => settings.UI.LanguageCode))
        {
            _settingsService.Update(settings => settings.UI.LanguageCode = value.Code, SettingsSection.UI);
            _localizationService.LoadLanguage(value.Code);
            OnPropertyChanged(nameof(AvailableThemes));
            var theme = _settingsService.Read(settings => settings.UI.Theme);
            SelectedTheme = AvailableThemes.FirstOrDefault(x => x.Value == theme) ?? AvailableThemes[0];
            _animeListViewModel.RefreshLocalization();
            if (Application.Current is App app) app.UpdateTrayMenu();
        }
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.AutoCheckUpdates = value, SettingsSection.System);
    }

    partial void OnAutoDownloadUpdatesChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.AutoDownloadUpdates = value, SettingsSection.System);
    }

    partial void OnNotifyNewEpisodesChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.NotifyNewEpisodes = value, SettingsSection.System);
    }

    partial void OnNotifyAppUpdateChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.NotifyAppUpdate = value, SettingsSection.System);
    }

    partial void OnNewEpisodeNotificationDelayMinutesChanged(decimal? value)
    {
        if (value.HasValue)
        {
            var minutes = (int)Math.Max(0, value.Value);
            _settingsService.Update(settings => settings.System.NewEpisodeNotificationDelayMinutes = minutes, SettingsSection.System);
        }
    }
}
