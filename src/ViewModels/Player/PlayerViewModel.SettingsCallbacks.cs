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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels.Player;

public partial class PlayerViewModel
{
    partial void OnPlayerAutoPlayChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.AutoPlay = value, SettingsSection.Player);
    }

    partial void OnSinglePlayerWindowChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SingleWindow = value, SettingsSection.Player);
    }

    partial void OnRememberPlayerVolumeChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings =>
        {
            settings.Player.RememberVolume = value;
            if (value) settings.Player.Volume = Math.Clamp(Volume, 0, 100);
        }, SettingsSection.Player);
    }

    partial void OnAutoHideControlsChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.AutoHideControls = value, SettingsSection.Player);
    }

    partial void OnShowChapterMarkersChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ShowChapterMarkers = value, SettingsSection.Player);
    }

    partial void OnLeftClickActionChanged(PlayerMouseActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.LeftClickAction = value.Value, SettingsSection.Player);
    }

    partial void OnRightClickActionChanged(PlayerMouseActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.RightClickAction = value.Value, SettingsSection.Player);
    }

    partial void OnMiddleClickActionChanged(PlayerMouseActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.MiddleClickAction = value.Value, SettingsSection.Player);
    }

    partial void OnWheelUpActionChanged(PlayerWheelActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.WheelUpAction = value.Value, SettingsSection.Player);
    }

    partial void OnWheelDownActionChanged(PlayerWheelActionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.WheelDownAction = value.Value, SettingsSection.Player);
    }

    partial void OnWheelVolumeStepChanged(int value)
    {
        var normalized = FindWheelStep(value);
        if (value != normalized)
        {
            WheelVolumeStep = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.WheelVolumeStep = normalized, SettingsSection.Player);
    }

    partial void OnSeekStepChanged(int value)
    {
        var normalized = FindSeekStep(value);
        if (value != normalized)
        {
            SeekStep = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SeekStep = normalized, SettingsSection.Player);
    }

    partial void OnShowPlayPauseButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowPlayPauseButton = value);
    partial void OnShowSkipButtonsChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSkipButtons = value);
    partial void OnShowMuteButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowMuteButton = value);
    partial void OnShowVolumeSliderChanged(bool value) => SavePlayerPanelButtons(x => x.ShowVolumeSlider = value);
    partial void OnShowTimeDisplayChanged(bool value) => SavePlayerPanelButtons(x => x.ShowTimeDisplay = value);
    partial void OnShowSpeedButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSpeedButton = value);
    partial void OnShowSubtitleButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSubtitleButton = value);
    partial void OnShowSubtitlePositionButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSubtitlePositionButton = value);
    partial void OnShowAudioButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowAudioButton = value);
    partial void OnShowScreenshotButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowScreenshotButton = value);
    partial void OnShowSubtitleStyleButtonChanged(bool value) => SavePlayerPanelButtons(x => x.ShowSubtitleStyleButton = value);
    partial void OnPreferredAudioLanguagesChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        PreferredAudioLanguages = NormalizeLanguageList(value, "Japanese,jpn,ja");
        _settingsService.Update(settings => settings.Player.PreferredAudioLanguages = PreferredAudioLanguages, SettingsSection.Player);
        ApplyTrackLanguagePreferences();
    }
    partial void OnPreferredSubtitleLanguagesChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        PreferredSubtitleLanguages = NormalizeLanguageList(value, "Russian,rus,ru");
        _settingsService.Update(settings => settings.Player.PreferredSubtitleLanguages = PreferredSubtitleLanguages, SettingsSection.Player);
        ApplyTrackLanguagePreferences();
    }
    partial void OnSubtitleStyleOverrideEnabledChanged(bool value)
    {
        if (!_isApplyingSettings)
            ShowOsd("Стиль субтитров", value ? "включен" : "выключен");

        if (_isApplyingSettings) return;
        ApplySubtitleStyleOverride();
        if (_settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SubtitleStyleOverrideEnabled = value, SettingsSection.Player);
    }
    partial void OnSubtitleStyleHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SubtitleStyleHotkey = NormalizeHotkey(value, "U"), SettingsSection.Player);
    }
    partial void OnSubtitleFontChanged(string value) => SaveSubtitleStyle(x => x.SubtitleFont = NormalizeMpvOption(value, "Candara Bold"));
    partial void OnSubtitleFontSizeChanged(double value) => SaveSubtitleStyle(x => x.SubtitleFontSize = Math.Clamp(value, 1, 300));
    partial void OnSubtitleColorChanged(string value) => SaveSubtitleStyle(x => x.SubtitleColor = NormalizeSubtitleColor(value, "#FFFFFF"));
    partial void OnSubtitleBorderColorChanged(string value) => SaveSubtitleStyle(x => x.SubtitleBorderColor = NormalizeSubtitleColor(value, "#000000"));
    partial void OnSubtitleShadowColorChanged(string value) => SaveSubtitleStyle(x => x.SubtitleShadowColor = NormalizeSubtitleColor(value, "#000000"));
    partial void OnSubtitleBorderSizeChanged(double value) => SaveSubtitleStyle(x => x.SubtitleBorderSize = Math.Clamp(value, 0, 20));
    partial void OnSubtitleShadowOffsetChanged(double value) => SaveSubtitleStyle(x => x.SubtitleShadowOffset = Math.Clamp(value, 0, 20));
    partial void OnSubtitleAlignYChanged(string value) => SaveSubtitleStyle(x => x.SubtitleAlignY = NormalizeSubtitleAlignY(value, "bottom"));
    partial void OnSubtitleAlignXChanged(string value) => SaveSubtitleStyle(x => x.SubtitleAlignX = NormalizeSubtitleAlignX(value, "center"));
    partial void OnSubtitleMarginYChanged(int value) => SaveSubtitleStyle(x => x.SubtitleMarginY = Math.Clamp(value, 0, 500));
    partial void OnSubtitleScaleByWindowChanged(bool value) => SaveSubtitleStyle(x => x.SubtitleScaleByWindow = value);
    partial void OnScreenshotDirectoryChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        var normalized = NormalizeScreenshotDirectory(value);
        _settingsService.Update(settings => settings.Player.ScreenshotDirectory = normalized, SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotFormatChanged(string value)
    {
        var normalized = NormalizeScreenshotFormat(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            ScreenshotFormat = normalized;

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotFormat = normalized, SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotResolutionChanged(ScreenshotResolutionOption? value)
    {
        if (_isApplyingSettings || _settingsService == null || value == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotResolutionMode = value.Value, SettingsSection.Player);
    }
    partial void OnScreenshotPngCompressionChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 9);
        if (value != normalized)
        {
            ScreenshotPngCompression = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotPngCompression = normalized, SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotQualityChanged(int value)
    {
        var normalized = Math.Clamp(value, 0, 100);
        if (value != normalized)
        {
            ScreenshotQuality = normalized;
            return;
        }

        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotQuality = normalized, SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotHighBitDepthChanged(bool value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotHighBitDepth = value, SettingsSection.Player);
        ApplyScreenshotOptions();
    }
    partial void OnScreenshotWithSubtitlesHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotWithSubtitlesHotkey = NormalizeHotkey(value, "S"), SettingsSection.Player);
    }
    partial void OnScreenshotWithoutSubtitlesHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.ScreenshotWithoutSubtitlesHotkey = NormalizeHotkey(value, "Shift+S"), SettingsSection.Player);
    }
    partial void OnTogglePlayPauseHotkeyChanged(string value)
    {
        SaveHotkey(value, "Space", (settings, hotkey) => settings.TogglePlayPauseHotkey = hotkey);
    }
    partial void OnToggleFullscreenHotkeyChanged(string value)
    {
        SaveHotkey(value, "F", (settings, hotkey) => settings.ToggleFullscreenHotkey = hotkey);
    }
    partial void OnExitFullscreenHotkeyChanged(string value)
    {
        SaveHotkey(value, "Escape", (settings, hotkey) => settings.ExitFullscreenHotkey = hotkey);
    }
    partial void OnToggleMuteHotkeyChanged(string value)
    {
        SaveHotkey(value, "M", (settings, hotkey) => settings.ToggleMuteHotkey = hotkey);
    }
    partial void OnCycleAudioHotkeyChanged(string value)
    {
        SaveHotkey(value, "A", (settings, hotkey) => settings.CycleAudioHotkey = hotkey);
    }
    partial void OnCycleSubtitleHotkeyChanged(string value)
    {
        SaveHotkey(value, "V", (settings, hotkey) => settings.CycleSubtitleHotkey = hotkey);
    }
    partial void OnPreviousMediaHotkeyChanged(string value)
    {
        SaveHotkey(value, "P", (settings, hotkey) => settings.PreviousMediaHotkey = hotkey);
    }
    partial void OnNextMediaHotkeyChanged(string value)
    {
        SaveHotkey(value, "N", (settings, hotkey) => settings.NextMediaHotkey = hotkey);
    }
    partial void OnSpeedDownHotkeyChanged(string value)
    {
        SaveHotkey(value, "OemOpenBrackets", (settings, hotkey) => settings.SpeedDownHotkey = hotkey);
    }
    partial void OnSpeedUpHotkeyChanged(string value)
    {
        SaveHotkey(value, "OemCloseBrackets", (settings, hotkey) => settings.SpeedUpHotkey = hotkey);
    }
    partial void OnVolumeUpHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.VolumeUpHotkey = NormalizeHotkey(value, "Up"), SettingsSection.Player);
    }
    partial void OnVolumeDownHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.VolumeDownHotkey = NormalizeHotkey(value, "Down"), SettingsSection.Player);
    }
    partial void OnSeekBackwardHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SeekBackwardHotkey = NormalizeHotkey(value, "Left"), SettingsSection.Player);
    }
    partial void OnSeekForwardHotkeyChanged(string value)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => settings.Player.SeekForwardHotkey = NormalizeHotkey(value, "Right"), SettingsSection.Player);
    }
    partial void OnMpvScaleChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvScale = NormalizeMpvOption(value, "ewa_lanczossharp"));
    partial void OnMpvChromaScaleChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvChromaScale = NormalizeMpvOption(value, "ewa_lanczossharp"));
    partial void OnMpvDitherDepthChanged(string value) =>
        SaveVideoProcessingOption(x => x.MpvDitherDepth = NormalizeMpvOption(value, "auto"));
    partial void OnMpvCorrectDownscalingChanged(bool value) =>
        SaveVideoProcessingOption(x => x.MpvCorrectDownscaling = value);
    partial void OnMpvDebandChanged(bool value) =>
        SaveVideoProcessingOption(x => x.MpvDeband = value);
    partial void OnMpvDebandIterationsChanged(int value) =>
        SaveVideoProcessingOption(x => x.MpvDebandIterations = Math.Clamp(value, 0, 16));
    partial void OnMpvDebandThresholdChanged(int value) =>
        SaveVideoProcessingOption(x => x.MpvDebandThreshold = Math.Clamp(value, 0, 4096));
    partial void OnMpvHwdecChanged(string value)
    {
        var normalized = NormalizeMpvOption(value, "auto");
        SaveMpvOption(x => x.MpvHwdec = normalized);
        _playback.SetOptionString("hwdec", normalized);
        RefreshMpvRuntimeInfo();
    }
    partial void OnMpvVideoOutputChanged(string value) =>
        SaveMpvOption(x => x.MpvVideoOutput = NormalizeMpvOption(value, "gpu-next"));
    partial void OnMpvGpuApiChanged(string value) =>
        SaveMpvOption(x => x.MpvGpuApi = NormalizeMpvOption(value, "auto"));
    partial void OnMpvGpuContextChanged(string value) =>
        SaveMpvOption(x => x.MpvGpuContext = NormalizeMpvOption(value, "auto"));

    private void SavePlayerPanelButtons(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), SettingsSection.Player);
    }

    private void SaveHotkey(string value, string fallback, Action<AppSettings.PlayerConfig, string> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        var normalized = NormalizeHotkey(value, fallback);
        _settingsService.Update(settings => update(settings.Player, normalized), SettingsSection.Player);
    }

    private void SaveMpvOption(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), SettingsSection.Player);
    }

    private void SaveVideoProcessingOption(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), SettingsSection.Player);
        ApplyVideoProcessingOptions();
    }

    private void SaveSubtitleStyle(Action<AppSettings.PlayerConfig> update)
    {
        if (_isApplyingSettings || _settingsService == null) return;
        _settingsService.Update(settings => update(settings.Player), SettingsSection.Player);
        ApplySubtitleStyleOverride();
    }

    private void ApplyScreenshotOptions()
    {
        _settingsApplier.ApplyScreenshot(new PlayerScreenshotOptions(
            ScreenshotDirectory,
            ScreenshotFormat,
            ScreenshotPngCompression,
            ScreenshotQuality,
            ScreenshotHighBitDepth));
    }

    private void ApplyTrackLanguagePreferences()
    {
        _settingsApplier.ApplyTrackLanguagePreferences(new PlayerTrackLanguageOptions(
            NormalizeLanguageList(PreferredAudioLanguages, "Japanese,jpn,ja"),
            NormalizeLanguageList(PreferredSubtitleLanguages, "Russian,rus,ru")));
    }

    private void ApplyVideoProcessingOptions()
    {
        _settingsApplier.ApplyVideoProcessing(new PlayerVideoProcessingOptions(
            NormalizeMpvOption(MpvScale, "ewa_lanczossharp"),
            NormalizeMpvOption(MpvChromaScale, "ewa_lanczossharp"),
            NormalizeMpvOption(MpvDitherDepth, "auto"),
            MpvCorrectDownscaling,
            MpvDeband,
            Math.Clamp(MpvDebandIterations, 0, 16),
            Math.Clamp(MpvDebandThreshold, 0, 4096)));
    }

    private void ApplySubtitleStyleOverride()
    {
        _settingsApplier.ApplySubtitleStyle(new PlayerSubtitleStyleOptions(
            SubtitleStyleOverrideEnabled,
            NormalizeMpvOption(SubtitleFont, "Candara Bold"),
            Math.Clamp(SubtitleFontSize, 1, 300),
            NormalizeSubtitleColor(SubtitleColor, "#FFFFFF"),
            NormalizeSubtitleColor(SubtitleBorderColor, "#000000"),
            NormalizeSubtitleColor(SubtitleShadowColor, "#000000"),
            Math.Clamp(SubtitleBorderSize, 0, 20),
            Math.Clamp(SubtitleShadowOffset, 0, 20),
            NormalizeSubtitleAlignY(SubtitleAlignY, "bottom"),
            NormalizeSubtitleAlignX(SubtitleAlignX, "center"),
            Math.Clamp(SubtitleMarginY, 0, 500),
            SubtitleScaleByWindow));
    }

    private static string NormalizeScreenshotDirectory(string? value)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(value))
            return desktop;

        var trimmed = value.Trim();
        var oldDefault = System.IO.Path.Combine(desktop, "Kiriha Screenshots");
        return string.Equals(trimmed, oldDefault, StringComparison.OrdinalIgnoreCase)
            ? desktop
            : trimmed;
    }

    private static string NormalizeScreenshotFormat(string? value)
    {
        if (string.Equals(value, "jpg", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "jpeg", StringComparison.OrdinalIgnoreCase))
            return "jpg";

        if (string.Equals(value, "webp", StringComparison.OrdinalIgnoreCase))
            return "webp";

        return "png";
    }

    private static string NormalizeSubtitleColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = $"#{trimmed}";

        var hex = trimmed[1..];
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit)
            ? $"#{hex.ToUpperInvariant()}"
            : fallback;
    }

    private static string NormalizeSubtitleAlignX(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed is "left" or "center" or "right"
            ? trimmed
            : fallback;
    }

    private static string NormalizeSubtitleAlignY(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed is "top" or "center" or "bottom"
            ? trimmed
            : fallback;
    }

    private static string NormalizeLanguageList(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length == 0 ? fallback : string.Join(",", parts);
    }

    private static string NormalizeHotkey(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
