namespace Kiriha.ViewModels.Player;

public sealed class PlayerSettingsApplier
{
    private readonly PlayerPlaybackController _playback;

    public PlayerSettingsApplier(PlayerPlaybackController playback)
    {
        _playback = playback;
    }

    public void ApplyScreenshot(PlayerScreenshotOptions options)
    {
        _playback.SetScreenshotOptions(
            options.Directory,
            options.Format,
            options.PngCompression,
            options.Quality,
            options.HighBitDepth);
    }

    public void ApplyTrackLanguagePreferences(PlayerTrackLanguageOptions options)
    {
        _playback.SetTrackLanguagePreferences(options.AudioLanguages, options.SubtitleLanguages);
    }

    public void ApplyVideoProcessing(PlayerVideoProcessingOptions options)
    {
        _playback.SetVideoProcessingOptions(
            options.Scale,
            options.ChromaScale,
            options.DitherDepth,
            options.CorrectDownscaling,
            options.Deband,
            options.DebandIterations,
            options.DebandThreshold);
    }

    public void ApplySubtitleStyle(PlayerSubtitleStyleOptions options)
    {
        _playback.SetSubtitleStyleOverride(
            options.Enabled,
            options.Font,
            options.FontSize,
            options.Color,
            options.BorderColor,
            options.ShadowColor,
            options.BorderSize,
            options.ShadowOffset,
            options.AlignY,
            options.AlignX,
            options.MarginY,
            options.ScaleByWindow);
    }
}

public readonly record struct PlayerScreenshotOptions(
    string Directory,
    string Format,
    int PngCompression,
    int Quality,
    bool HighBitDepth);

public readonly record struct PlayerTrackLanguageOptions(
    string AudioLanguages,
    string SubtitleLanguages);

public readonly record struct PlayerVideoProcessingOptions(
    string Scale,
    string ChromaScale,
    string DitherDepth,
    bool CorrectDownscaling,
    bool Deband,
    int DebandIterations,
    int DebandThreshold);

public readonly record struct PlayerSubtitleStyleOptions(
    bool Enabled,
    string Font,
    double FontSize,
    string Color,
    string BorderColor,
    string ShadowColor,
    double BorderSize,
    double ShadowOffset,
    string AlignY,
    string AlignX,
    int MarginY,
    bool ScaleByWindow);
