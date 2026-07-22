namespace Kiriha.Core;

public static class SyncSafetyConstants
{
    public const int MinimumStatusGuardCount = 10;
    public const int MinimumStatusDropCount = 5;
    public const double MaximumAllowedStatusDropRatio = 0.30;
    public const double MaxAllowedTotalDropRatio = 0.70;
}
