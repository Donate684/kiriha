namespace Kiriha.Core.Mpv;

public sealed record MpvOptions(
    string Hwdec,
    string VideoOutput,
    string GpuApi,
    string GpuContext)
{
    public static MpvOptions Default { get; } = new("auto", "gpu-next", "auto", "auto");
}

