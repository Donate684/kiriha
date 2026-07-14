using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Services.Data;

namespace Kiriha.ViewModels;

public partial class CacheCleanupItem : ObservableObject
{
    public CacheCleanupTarget Target { get; }
    public string TitleKey { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStats))]
    private int _itemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayStats))]
    private long _sizeBytes;

    public string DisplayStats => SizeBytes > 0
        ? UIUtils.GetLoc("settings.cache.stats_with_size", ItemCount, FormatBytes(SizeBytes))
        : UIUtils.GetLoc("settings.cache.stats_items", ItemCount);

    public CacheCleanupItem(CacheCleanupTarget target, string titleKey)
    {
        Target = target;
        TitleKey = titleKey;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.##} {units[unit]}";
    }
}
