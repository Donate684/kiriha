using System;

namespace Kiriha.Core.Mpv;

public class MpvVideoPipelineConfigurator
{
    private readonly MpvPlayer _player;

    public MpvVideoPipelineConfigurator(MpvPlayer player)
    {
        _player = player;
    }

    internal void ConfigureVideoPipeline(IntPtr handle, MpvOptions options)
    {
        SetOptionalStringOption(handle, "hwdec", options.Hwdec, "set hardware decoder");
        SetOptionalStringOption(handle, "vo", "libmpv", "set video output");
        SetOptionalStringOption(handle, "gpu-api", options.GpuApi, "set GPU API");
        SetOptionalStringOption(handle, "gpu-context", options.GpuContext, "set GPU context");
    }

    private void SetOptionalStringOption(IntPtr handle, string name, string? value, string action)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        MpvPlayer.Check(LibMpvNative.mpv_set_option_string(handle, name, value.Trim()), action);
    }

    public void SetVideoProcessingOptions(
        string scale,
        string chromaScale,
        string ditherDepth,
        bool correctDownscaling,
        bool deband,
        int debandIterations,
        int debandThreshold)
    {
        _player.Enqueue(handle =>
        {
            MpvPlayer.SetMpvOption(handle, "scale", scale, "set video scale filter");
            MpvPlayer.SetMpvOption(handle, "cscale", chromaScale, "set chroma scale filter");
            MpvPlayer.SetMpvOption(handle, "dither-depth", ditherDepth, "set dither depth");
            MpvPlayer.SetMpvOption(handle, "correct-downscaling", correctDownscaling ? "yes" : "no", "set correct downscaling");
            MpvPlayer.SetMpvOption(handle, "deband", deband ? "yes" : "no", "set debanding");
            MpvPlayer.SetMpvOption(handle, "deband-iterations", Math.Clamp(debandIterations, 0, 16).ToString(System.Globalization.CultureInfo.InvariantCulture), "set deband iterations");
            MpvPlayer.SetMpvOption(handle, "deband-threshold", Math.Clamp(debandThreshold, 0, 4096).ToString(System.Globalization.CultureInfo.InvariantCulture), "set deband threshold");
        });
    }
}
