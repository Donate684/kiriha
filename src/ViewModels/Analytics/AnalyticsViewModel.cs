using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;

namespace Kiriha.ViewModels.Analytics;


public partial class AnalyticsViewModel : ViewModelBase
{
    private const int RecentHistoryDays = 14;

    private readonly AnimeService _animeService;
    private readonly HistoryService _historyService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ObservableCollection<AnalyticsMetric> Metrics { get; } = new();
    public ObservableCollection<AnalyticsBar> StatusDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> ScoreDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> GenreDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> StudioDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> YearDistribution { get; } = new();
    public ObservableCollection<AnalyticsBar> ReleaseYearCompletions { get; } = new();
    public ObservableCollection<AnalyticsBar> TasteHighlights { get; } = new();
    public ObservableCollection<AnalyticsFavoriteRow> FavoriteGenres { get; } = new();
    public ObservableCollection<AnalyticsFavoriteRow> FavoriteStudios { get; } = new();
    public ObservableCollection<ProfileTodoItem> WatchTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> FinishedWatchTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> UpcomingTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> PlanTodo { get; } = new();
    public ObservableCollection<ProfileTodoItem> StaleTodo { get; } = new();
    public ObservableCollection<AnalyticsDailyHistoryPoint> RecentHistory { get; } = new();
    public ObservableCollection<AnalyticsMonthlyHistoryRow> MonthlyHistory { get; } = new();
    public IReadOnlyList<string> MonthHeaders { get; } = new[]
    {
        "Jan", "Feb", "Mar", "Apr", "May", "Jun",
        "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
    };

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _updatedAt = string.Empty;
    [ObservableProperty] private int _selectedSection;
    [ObservableProperty] private int _recentHistoryEpisodes;
    [ObservableProperty] private int _recentHistoryTitles;
    [ObservableProperty] private bool _hasMonthlyHistory;
    [ObservableProperty] private bool _isHistoryPopupOpen;
    [ObservableProperty] private string _historyPopupTitle = string.Empty;
    [ObservableProperty] private string _historyPopupSubtitle = string.Empty;

    public ObservableCollection<AnalyticsHistoryEntry> HistoryPopupEntries { get; } = new();

    public bool IsOverviewSelected
    {
        get => SelectedSection == 0;
        set { if (value) SelectedSection = 0; }
    }

    public bool IsRatingsSelected
    {
        get => SelectedSection == 1;
        set { if (value) SelectedSection = 1; }
    }

    public bool IsTasteSelected
    {
        get => SelectedSection == 2;
        set { if (value) SelectedSection = 2; }
    }

    public bool IsWatchNextSelected
    {
        get => SelectedSection == 3;
        set { if (value) SelectedSection = 3; }
    }

    public bool IsHistorySelected
    {
        get => SelectedSection == 4;
        set { if (value) SelectedSection = 4; }
    }

    public AnalyticsViewModel(AnimeService animeService, HistoryService historyService)
    {
        _animeService = animeService;
        _historyService = historyService;
    }

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsRatingsSelected));
        OnPropertyChanged(nameof(IsTasteSelected));
        OnPropertyChanged(nameof(IsWatchNextSelected));
        OnPropertyChanged(nameof(IsHistorySelected));
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            IsRefreshing = true;

            var items = _animeService.Collection.ToList();
            var history = await _historyService.GetHistoryAsync(5000);
            HasData = items.Count > 0;
            UpdatedAt = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
            ClearAnalyticsCollections();

            if (!HasData)
            {
                return;
            }

            var nonPlanned = items.Where(x => x.Status != UserAnimeStatus.PlanToWatch && x.Status != UserAnimeStatus.None && x.Status != UserAnimeStatus.Dropped).ToList();
            var completed = items.Where(x => x.Status == UserAnimeStatus.Completed).ToList();
            var scored = nonPlanned
                .Select(x => int.TryParse(x.Score, out var score) ? score : 0)
                .Where(x => x > 0)
                .ToList();

            var totalEpisodes = items.Sum(x => Math.Max(0, x.Progress));
            var approximateHours = EstimateHoursWatched(items);
            var meanScore = scored.Count > 0 ? scored.Average() : 0;
            var completionRate = items.Count > 0 ? completed.Count * 100.0 / items.Count : 0;

            Metrics.Add(new AnalyticsMetric { Label = "Всего тайтлов", Value = items.Count.ToString("N0"), Hint = "в локальной библиотеке" });
            Metrics.Add(new AnalyticsMetric { Label = "Завершено", Value = completed.Count.ToString("N0"), Hint = $"{completionRate:0.#}% списка" });
            Metrics.Add(new AnalyticsMetric { Label = "Средняя оценка", Value = scored.Count > 0 ? meanScore.ToString("0.00") : "-", Hint = $"{scored.Count:N0} оценок" });
            Metrics.Add(new AnalyticsMetric { Label = "Эпизодов", Value = totalEpisodes.ToString("N0"), Hint = $"примерно {approximateHours:N0} ч" });

            AddStatusDistribution(items);
            AddScoreDistribution(scored);
            AddTopDistribution(GenreDistribution, nonPlanned.SelectMany(x => x.Genres), 8);
            AddTopDistribution(StudioDistribution, nonPlanned.SelectMany(x => x.Studios), 8);
            AddTasteHighlights();
            AddFavoriteRows(FavoriteGenres, nonPlanned, x => x.Genres, LocalizeGenre);
            AddFavoriteRows(FavoriteStudios, nonPlanned, x => x.Studios);
            AddProfileTodos(items);
            AddYearDistribution(completed);
            AddReleaseYearCompletions(completed);
            AddRecentHistory(history, items);
            AddMonthlyHistory(completed);
        }
        finally
        {
            IsRefreshing = false;
            _refreshGate.Release();
        }
    }

}
