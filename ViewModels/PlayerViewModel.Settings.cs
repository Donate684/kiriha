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
using Kiriha.Core.Mpv;
using Kiriha.Models;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels;

public partial class PlayerViewModel
{
    private void ApplyPlayerSettings()
    {
        if (_settingsService == null)
            return;

        _isApplyingSettings = true;
        try
        {
            var settings = _settingsService.Current.Player;
            PlayerAutoPlay = settings.AutoPlay;
            SinglePlayerWindow = settings.SingleWindow;
            RememberPlayerVolume = settings.RememberVolume;
            NormalizeAudio = settings.NormalizeAudio;
            AutoHideControls = settings.AutoHideControls;
            ShowChapterMarkers = settings.ShowChapterMarkers;
            LeftClickAction = FindMouseAction(settings.LeftClickAction);
            RightClickAction = FindMouseAction(settings.RightClickAction);
            MiddleClickAction = FindMouseAction(settings.MiddleClickAction);
            WheelUpAction = FindWheelAction(settings.WheelUpAction);
            WheelDownAction = FindWheelAction(settings.WheelDownAction);
            WheelVolumeStep = FindWheelStep(settings.WheelVolumeStep);
            ShowPlayPauseButton = settings.ShowPlayPauseButton;
            ShowSkipButtons = settings.ShowSkipButtons;
            ShowMuteButton = settings.ShowMuteButton;
            ShowVolumeSlider = settings.ShowVolumeSlider;
            ShowTimeDisplay = settings.ShowTimeDisplay;
            ShowSpeedButton = settings.ShowSpeedButton;
            ShowSubtitleButton = settings.ShowSubtitleButton;
            ShowSubtitlePositionButton = settings.ShowSubtitlePositionButton;
            ShowAudioButton = settings.ShowAudioButton;
            ShowScreenshotButton = settings.ShowScreenshotButton;
            ShowSubtitleStyleButton = settings.ShowSubtitleStyleButton;
            PreferredAudioLanguages = NormalizeLanguageList(settings.PreferredAudioLanguages, "Japanese,jpn,ja");
            PreferredSubtitleLanguages = NormalizeLanguageList(settings.PreferredSubtitleLanguages, "Russian,rus,ru");
            SubtitleStyleOverrideEnabled = settings.SubtitleStyleOverrideEnabled;
            SubtitleStyleHotkey = NormalizeHotkey(settings.SubtitleStyleHotkey, "U");
            SubtitleFont = NormalizeMpvOption(settings.SubtitleFont, "Candara Bold");
            SubtitleFontSize = Math.Clamp(settings.SubtitleFontSize, 1, 300);
            SubtitleColor = NormalizeSubtitleColor(settings.SubtitleColor, "#FFFFFF");
            SubtitleBorderColor = NormalizeSubtitleColor(settings.SubtitleBorderColor, "#000000");
            SubtitleShadowColor = NormalizeSubtitleColor(settings.SubtitleShadowColor, "#000000");
            SubtitleBorderSize = Math.Clamp(settings.SubtitleBorderSize, 0, 20);
            SubtitleShadowOffset = Math.Clamp(settings.SubtitleShadowOffset, 0, 20);
            SubtitleAlignY = NormalizeSubtitleAlignY(settings.SubtitleAlignY, "bottom");
            SubtitleAlignX = NormalizeSubtitleAlignX(settings.SubtitleAlignX, "center");
            SubtitleMarginY = Math.Clamp(settings.SubtitleMarginY, 0, 500);
            SubtitleScaleByWindow = settings.SubtitleScaleByWindow;
            ScreenshotDirectory = NormalizeScreenshotDirectory(settings.ScreenshotDirectory);
            ScreenshotFormat = NormalizeScreenshotFormat(settings.ScreenshotFormat);
            ScreenshotResolution = FindScreenshotResolution(settings.ScreenshotResolutionMode);
            ScreenshotPngCompression = Math.Clamp(settings.ScreenshotPngCompression, 0, 9);
            ScreenshotQuality = Math.Clamp(settings.ScreenshotQuality, 0, 100);
            ScreenshotHighBitDepth = settings.ScreenshotHighBitDepth;
            ScreenshotWithSubtitlesHotkey = NormalizeHotkey(settings.ScreenshotWithSubtitlesHotkey, "S");
            ScreenshotWithoutSubtitlesHotkey = NormalizeHotkey(settings.ScreenshotWithoutSubtitlesHotkey, "Shift+S");
            TogglePlayPauseHotkey = NormalizeHotkey(settings.TogglePlayPauseHotkey, "Space");
            ToggleFullscreenHotkey = NormalizeHotkey(settings.ToggleFullscreenHotkey, "F");
            ExitFullscreenHotkey = NormalizeHotkey(settings.ExitFullscreenHotkey, "Escape");
            ToggleMuteHotkey = NormalizeHotkey(settings.ToggleMuteHotkey, "M");
            CycleAudioHotkey = NormalizeHotkey(settings.CycleAudioHotkey, "A");
            CycleSubtitleHotkey = NormalizeHotkey(settings.CycleSubtitleHotkey, "V");
            PreviousMediaHotkey = NormalizeHotkey(settings.PreviousMediaHotkey, "P");
            NextMediaHotkey = NormalizeHotkey(settings.NextMediaHotkey, "N");
            SpeedDownHotkey = NormalizeHotkey(settings.SpeedDownHotkey, "OemOpenBrackets");
            SpeedUpHotkey = NormalizeHotkey(settings.SpeedUpHotkey, "OemCloseBrackets");
            VolumeUpHotkey = NormalizeHotkey(settings.VolumeUpHotkey, "Up");
            VolumeDownHotkey = NormalizeHotkey(settings.VolumeDownHotkey, "Down");
            SeekBackwardHotkey = NormalizeHotkey(settings.SeekBackwardHotkey, "Left");
            SeekForwardHotkey = NormalizeHotkey(settings.SeekForwardHotkey, "Right");
            MpvScale = NormalizeMpvOption(settings.MpvScale, "ewa_lanczossharp");
            MpvChromaScale = NormalizeMpvOption(settings.MpvChromaScale, "ewa_lanczossharp");
            MpvDitherDepth = NormalizeMpvOption(settings.MpvDitherDepth, "auto");
            MpvCorrectDownscaling = settings.MpvCorrectDownscaling;
            MpvDeband = settings.MpvDeband;
            MpvDebandIterations = Math.Clamp(settings.MpvDebandIterations, 0, 16);
            MpvDebandThreshold = Math.Clamp(settings.MpvDebandThreshold, 0, 4096);
            MpvHwdec = NormalizeMpvOption(settings.MpvHwdec, "auto");
            MpvVideoOutput = NormalizeMpvOption(settings.MpvVideoOutput, "gpu-next");
            MpvGpuApi = NormalizeMpvOption(settings.MpvGpuApi, "auto");
            MpvGpuContext = NormalizeMpvOption(settings.MpvGpuContext, "auto");
            Volume = Math.Clamp(settings.Volume, 0, 100);
            PlaybackSpeed = Math.Clamp(settings.PlaybackSpeed, 0.1, 4.0);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private PlayerMouseActionOption FindMouseAction(PlayerMouseAction action)
    {
        return MouseActionOptions.FirstOrDefault(x => x.Value == action) ?? MouseActionOptions[0];
    }

    private PlayerWheelActionOption FindWheelAction(PlayerWheelAction action)
    {
        return WheelActionOptions.FirstOrDefault(x => x.Value == action) ?? WheelActionOptions[0];
    }

    private int FindWheelStep(int step)
    {
        return WheelStepOptions.Contains(step) ? step : 5;
    }

    private ScreenshotResolutionOption FindScreenshotResolution(string? value)
    {
        return ScreenshotResolutionOptions.FirstOrDefault(x => string.Equals(x.Value, value, StringComparison.OrdinalIgnoreCase))
               ?? ScreenshotResolutionOptions[0];
    }

    partial void OnVideoUrlChanged(string value)
    {
        OnPropertyChanged(nameof(TrackingTitle));

        if (string.IsNullOrEmpty(value) || _isInitializing) return;

        ApplyMetadata(_metadataResolver?.Resolve(value) ?? PlayerMediaMetadata.FromVideoPath(value));
        UpdateNavigationAvailability();
    }

    private void ApplyMetadata(PlayerMediaMetadata metadata)
    {
        _animeId = metadata.AnimeId;
        AnimeTitleRu = metadata.TitleRu;
        AnimeTitleEn = metadata.TitleEn;
        RawEpisodeText = metadata.EpisodeText;
        EpisodeTitle = string.IsNullOrEmpty(metadata.EpisodeText)
            ? string.Empty
            : $"\u0421\u0435\u0440\u0438\u044F {metadata.EpisodeText}";
        AnimeTitle = AnimeTitleRu;
        OnPropertyChanged(nameof(TrackingTitle));
    }

    public void ApplyExternalMetadata(PlayerMediaMetadata metadata)
    {
        ApplyMetadata(metadata);
        _statePublisher.Publish();
    }

    public bool MatchesOriginalTitle(string originalTitle)
    {
        if (string.IsNullOrWhiteSpace(originalTitle))
            return true;

        var current = System.IO.Path.GetFileNameWithoutExtension(VideoUrl);
        return string.Equals(current, originalTitle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Window title used by external now-playing detectors. Keep the raw filename here:
    /// parsed display metadata can lose release/season/episode details that Anitomy needs.
    /// </summary>
    public string TrackingTitle
    {
        get
        {
            var filename = System.IO.Path.GetFileNameWithoutExtension(VideoUrl);
            if (!string.IsNullOrWhiteSpace(filename))
                return $"[KirihaPlayer] {filename}";

            var title = !string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu;
            return $"[KirihaPlayer] {title}";
        }
    }

    public string TopTitle
    {
        get
        {
            return !string.IsNullOrEmpty(AnimeTitleEn) ? AnimeTitleEn : AnimeTitleRu;
        }
    }

    public string BottomTitle
    {
        get
        {
            if (!string.IsNullOrEmpty(AnimeTitleEn) && AnimeTitleEn != AnimeTitleRu)
                return AnimeTitleRu;
            return string.Empty;
        }
    }

    public bool HasBottomTitle => !string.IsNullOrEmpty(BottomTitle);
    public bool HasEpisodeAndBottom => !string.IsNullOrEmpty(EpisodeTitle) && HasBottomTitle;

    partial void OnAnimeTitleRuChanged(string value)
    {
        OnPropertyChanged(nameof(TopTitle));
        OnPropertyChanged(nameof(BottomTitle));
        OnPropertyChanged(nameof(HasBottomTitle));
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }
    partial void OnAnimeTitleEnChanged(string value)
    {
        OnPropertyChanged(nameof(TopTitle));
        OnPropertyChanged(nameof(BottomTitle));
        OnPropertyChanged(nameof(HasBottomTitle));
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }
    partial void OnEpisodeTitleChanged(string value)
    {
        OnPropertyChanged(nameof(HasEpisodeAndBottom));
    }

    private static string NormalizeMpvOption(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
