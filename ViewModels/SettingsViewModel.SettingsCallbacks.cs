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
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;

namespace Kiriha.ViewModels;

public partial class SettingsViewModel
{
    partial void OnSelectedThemeChanged(ThemeOption value)
    {
        if (Application.Current == null || value == null) return;
        _settingsService.Update(settings => settings.UI.Theme = value.Value);

        Application.Current.RequestedThemeVariant = value.Value switch
        {
            ThemeType.Light => ThemeVariant.Light,
            ThemeType.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    partial void OnSelectedCardStyleChanged(int value)
    {
        _settingsService.Update(settings => settings.UI.CardStyle = value);
        CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.Send(new CardStyleChangedMessage(value));
    }

    partial void OnUseFiveStarRatingChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseFiveStarRating = value);
        _animeListViewModel.RefreshAvailableScores();
    }

    partial void OnUseRussianTitlesChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseRussianTitles = value);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnUseRussianDescriptionsChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.UseRussianDescriptions = value);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnShowAiringInfoChanged(bool value)
    {
        _settingsService.Update(settings => settings.UI.ShowAiringInfo = value);
        _animeListViewModel.RefreshLocalization();
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.CloseToTray = value);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.MinimizeToTray = value);
    }

    partial void OnKeepPlayerProcessAliveChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.KeepPlayerProcessAlive = value);

        if (value) PlayerProcessBridge.StartResident();
        else _ = PlayerProcessBridge.StopResidentAsync();
    }

    partial void OnSinglePlayerWindowChanged(bool value)
    {
        _settingsService.Update(settings => settings.Player.SingleWindow = value);
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
        _settingsService.Update(settings => update(settings.Player));
    }

    private static string NormalizeMpvOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    partial void OnEnableScrobblerChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.Scrobbler.Enabled = value);
    }

    partial void OnScrobbleDelaySecondsChanged(decimal? value)
    {
        if (value.HasValue)
        {
            _settingsService.Update(settings => settings.System.Scrobbler.DelaySeconds = (int)value.Value);
        }
    }

    partial void OnScrobbleNotifyOnSkipChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.Scrobbler.NotifyOnSkippedEpisode = value);
    }

    partial void OnEnableDiscordRPCChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.EnableDiscordRPC = value);
        // Update Discord service status
        _discordService.UpdateStatus(value);
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value != null && value.Code != _settingsService.Read(settings => settings.UI.LanguageCode))
        {
            _settingsService.Update(settings => settings.UI.LanguageCode = value.Code);
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
        _settingsService.Update(settings => settings.System.AutoCheckUpdates = value);
    }

    partial void OnAutoDownloadUpdatesChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.AutoDownloadUpdates = value);
    }

    partial void OnNotifyNewEpisodesChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.NotifyNewEpisodes = value);
    }

    partial void OnNotifyAppUpdateChanged(bool value)
    {
        _settingsService.Update(settings => settings.System.NotifyAppUpdate = value);
    }

    partial void OnNewEpisodeNotificationDelayMinutesChanged(decimal? value)
    {
        if (value.HasValue)
        {
            var minutes = (int)Math.Max(0, value.Value);
            _settingsService.Update(settings => settings.System.NewEpisodeNotificationDelayMinutes = minutes);
        }
    }
}
