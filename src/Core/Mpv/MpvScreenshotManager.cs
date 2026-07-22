using System;
using System.IO;

namespace Kiriha.Core.Mpv;

public class MpvScreenshotManager
{
    private readonly MpvPlayer _player;
    private string _screenshotDirectory = GetDefaultScreenshotDirectory();
    private string _screenshotFormat = "png";

    public MpvScreenshotManager(MpvPlayer player)
    {
        _player = player;
    }

    public void TakeScreenshot(bool includeSubtitles, string resolutionMode)
    {
        var flag = string.Equals(resolutionMode, "window", StringComparison.OrdinalIgnoreCase)
            ? "window"
            : includeSubtitles ? "subtitles" : "video";

        _player.Enqueue(handle =>
        {
            Directory.CreateDirectory(_screenshotDirectory);
            var filename = $"Kiriha-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{_screenshotFormat}";
            var path = Path.Combine(_screenshotDirectory, filename);
            MpvPlayer.Check(LibMpvNative.mpv_command_string(handle, "screenshot-to-file", path, flag), "take screenshot");
        });
    }

    public void SetScreenshotOptions(
        string directory,
        string format,
        int pngCompression,
        int quality,
        bool highBitDepth)
    {
        _player.Enqueue(handle => ConfigureScreenshots(
            handle,
            directory,
            format,
            pngCompression,
            quality,
            highBitDepth));
    }

    internal void ConfigureScreenshots(
        IntPtr handle,
        string? directory = null,
        string format = "png",
        int pngCompression = 4,
        int quality = 95,
        bool highBitDepth = false)
    {
        var screenshotDir = string.IsNullOrWhiteSpace(directory)
            ? GetDefaultScreenshotDirectory()
            : directory;
        var screenshotFormat = NormalizeScreenshotFormat(format);

        Directory.CreateDirectory(screenshotDir);
        _screenshotDirectory = screenshotDir;
        _screenshotFormat = screenshotFormat;
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-directory", screenshotDir), "set screenshot directory");
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-template", "Kiriha-%F-%P"), "set screenshot template");
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-format", screenshotFormat), "set screenshot format");
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-png-compression", Math.Clamp(pngCompression, 0, 9).ToString(System.Globalization.CultureInfo.InvariantCulture)), "set screenshot png compression");
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-jpeg-quality", Math.Clamp(quality, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)), "set screenshot jpeg quality");
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-webp-quality", Math.Clamp(quality, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)), "set screenshot webp quality");
        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, "screenshot-high-bit-depth", highBitDepth ? "yes" : "no"), "set screenshot bit depth");
    }

    private static string GetDefaultScreenshotDirectory()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return desktop;
    }

    private static string NormalizeScreenshotFormat(string? format)
    {
        return string.Equals(format, "jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(format, "jpeg", StringComparison.OrdinalIgnoreCase)
            ? "jpg"
            : string.Equals(format, "webp", StringComparison.OrdinalIgnoreCase)
                ? "webp"
                : "png";
    }
}
