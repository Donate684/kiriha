namespace Kiriha.Core.Mpv;

public sealed record PlaybackState(
    double Position,
    double Duration,
    bool IsPlaying,
    bool IsSeekable,
    bool IsLoaded);
