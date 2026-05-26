using System.Collections.ObjectModel;

namespace Kiriha.ViewModels;

public sealed class AnalyticsMetric
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
}

public sealed class AnalyticsBar
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public double Percent { get; init; }
    public double Alpha { get; init; } = 1;
    public int Count { get; init; }
    public string Accent { get; init; } = string.Empty;
    public string TextColor { get; init; } = string.Empty;
    public string ShareText { get; init; } = string.Empty;
    public double BarHeight { get; init; }
}

public sealed class AnalyticsFavoriteRow
{
    public int Rank { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public string MeanScore { get; init; } = "-";
    public string WeightedScore { get; init; } = "-";
    public string TimeSpent { get; init; } = "0h";
    public string Summary { get; init; } = string.Empty;
    public double Percent { get; init; }
    public string Accent { get; init; } = "#FF2D7DD2";
    public ObservableCollection<AnalyticsHistoryEntry> Entries { get; } = new();
}

public sealed class ProfileTodoItem
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string Badge { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string? PosterUrl { get; init; }
    public string Accent { get; init; } = "#FF2D7DD2";
}

public sealed class AnalyticsDailyHistoryPoint
{
    public int DaysAgo { get; init; }
    public string Label { get; init; } = string.Empty;
    public string DateLabel { get; init; } = string.Empty;
    public int Count { get; init; }
    public double BarHeight { get; init; }
    public double Alpha { get; init; }
    public string CountLabel { get; init; } = string.Empty;
    public bool ShowCountInBar { get; init; }
    public bool HasCount => Count > 0;
    public string Tooltip { get; init; } = string.Empty;
    public ObservableCollection<AnalyticsHistoryEntry> Entries { get; } = new();
}

public sealed class AnalyticsMonthlyHistoryCell
{
    public int Month { get; init; }
    public string MonthName { get; init; } = string.Empty;
    public int Count { get; init; }
    public double Alpha { get; init; }
    public string Fill { get; init; } = string.Empty;
    public string TextColor { get; init; } = string.Empty;
    public bool IsCurrentMonth { get; init; }
    public bool HasCount => Count > 0;
    public string Tooltip { get; init; } = string.Empty;
    public ObservableCollection<AnalyticsHistoryEntry> Entries { get; } = new();
}

public sealed class AnalyticsMonthlyHistoryRow
{
    public int Year { get; init; }
    public ObservableCollection<AnalyticsMonthlyHistoryCell> Months { get; } = new();
}

public sealed class AnalyticsHistoryEntry
{
    public string Title { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string? PosterUrl { get; init; }
}
