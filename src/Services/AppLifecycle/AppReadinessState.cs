namespace Kiriha.Services.AppLifecycle;

public enum AppReadinessState
{
    NotStarted,
    Starting,
    Ready,
    Failed
}
