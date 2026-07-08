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
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Core.Dialogs;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;

namespace Kiriha.ViewModels.History;

public class HistoryGroup
{
    public string Header { get; set; } = string.Empty;
    public ObservableCollection<HistoryEntryVm> Items { get; } = new();
}

/// <summary>
/// Display entry for history. Represents either a single HistoryItem or a
/// merged range of consecutive episode-watches for the same anime on the same day.
/// </summary>
public class HistoryEntryVm
{
    public int AnimeId { get; set; }
    public string AnimeTitle { get; set; } = string.Empty;
    public string? RussianTitle { get; set; }
    public string? PosterUrl { get; set; }
    public int ActionType { get; set; }
    public string Detail { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }            // Most recent action in the group (for time display)
    public int EpisodeFrom { get; set; }
    public int EpisodeTo { get; set; }
    public int Count { get; set; } = 1;                // How many raw entries were merged
    public HistoryItem? Primary { get; set; }          // Representative raw item (for OpenDetails command)

    public bool IsRange => EpisodeFrom != EpisodeTo;
    public string EpisodeLabel =>
        (ActionType == 1 || ActionType == 4 || ActionType == 6)
            ? (IsRange
                ? UIUtils.GetLoc("history.episode_range", EpisodeFrom, EpisodeTo)
                : (EpisodeFrom > 0 ? UIUtils.GetLoc("history.episode_single", EpisodeFrom) : string.Empty))
            : string.Empty;
}

public partial class HistoryViewModel : ViewModelBase
{
    // Constructor-injected: explicit dependencies, testable, no static service locator.
    private readonly HistoryService _historyService;
    private readonly DatabaseInitializer _dbInit;
    private readonly AnimeService _animeService;
    private readonly MalApiService _malApi;
    private readonly IDialogService _dialogs;
    private readonly SettingsService _settings;
    private List<HistoryItem> _rawItems = new();

    [ObservableProperty]
    private ObservableCollection<HistoryGroup> _groupedHistory = new();

    [ObservableProperty]
    private bool _hasHistory;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>0=All, 1=Today, 2=Week, 3=Month</summary>
    [ObservableProperty]
    private int _selectedPeriod;

    /// <summary>0=All, or one of HistoryItem.ActionType values.</summary>
    [ObservableProperty]
    private int _selectedAction;

    public HistoryViewModel(
        HistoryService historyService,
        DatabaseInitializer dbInit,
        AnimeService animeService,
        MalApiService malApi,
        IDialogService dialogs,
        SettingsService settings)
    {
        _historyService = historyService;
        _dbInit = dbInit;
        _animeService = animeService;
        _malApi = malApi;
        _dialogs = dialogs;
        _settings = settings;
        RefreshHistory().SafeFireAndForget("HistoryInit");
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilters();
    partial void OnSelectedPeriodChanged(int value)
    {
        ApplyFilters();
        NotifyPeriodFlags();
    }
    partial void OnSelectedActionChanged(int value)
    {
        ApplyFilters();
        NotifyActionFlags();
    }

    // ─── Radio-button friendly flags (also safe for ToggleButton: can't uncheck) ───
    public bool IsPeriodAll   { get => SelectedPeriod == 0; set { if (value) SelectedPeriod = 0; else OnPropertyChanged(nameof(IsPeriodAll)); } }
    public bool IsPeriodToday { get => SelectedPeriod == 1; set { if (value) SelectedPeriod = 1; else OnPropertyChanged(nameof(IsPeriodToday)); } }
    public bool IsPeriodWeek  { get => SelectedPeriod == 2; set { if (value) SelectedPeriod = 2; else OnPropertyChanged(nameof(IsPeriodWeek)); } }
    public bool IsPeriodMonth { get => SelectedPeriod == 3; set { if (value) SelectedPeriod = 3; else OnPropertyChanged(nameof(IsPeriodMonth)); } }

    public bool IsActionAll      { get => SelectedAction == 0; set { if (value) SelectedAction = 0; } }
    public bool IsActionWatched  { get => SelectedAction == 1; set { if (value) SelectedAction = 1; } }
    public bool IsActionCompleted{ get => SelectedAction == 6; set { if (value) SelectedAction = 6; } }
    public bool IsActionDropped  { get => SelectedAction == 7; set { if (value) SelectedAction = 7; } }
    public bool IsActionScoreSet { get => SelectedAction == 5; set { if (value) SelectedAction = 5; } }

    private void NotifyPeriodFlags()
    {
        OnPropertyChanged(nameof(IsPeriodAll));
        OnPropertyChanged(nameof(IsPeriodToday));
        OnPropertyChanged(nameof(IsPeriodWeek));
        OnPropertyChanged(nameof(IsPeriodMonth));
    }

    private void NotifyActionFlags()
    {
        OnPropertyChanged(nameof(IsActionAll));
        OnPropertyChanged(nameof(IsActionWatched));
        OnPropertyChanged(nameof(IsActionCompleted));
        OnPropertyChanged(nameof(IsActionDropped));
        OnPropertyChanged(nameof(IsActionScoreSet));
    }

    [RelayCommand]
    public async Task RefreshHistory()
    {
        await _dbInit.InitializationTask;

        _rawItems = await _historyService.GetHistoryAsync();

        // Resolve posters from the local user collection (cheap dictionary lookup).
        try
        {
            var collection = _animeService.Collection;
            var posterMap = collection
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (var item in _rawItems)
            {
                if (posterMap.TryGetValue(item.AnimeId, out var anime))
                {
                    item.PosterUrl = anime.MainPictureUrl;
                }
            }
        }
        catch { /* AnimeService may not be ready at very early startup */ }

        HasHistory = _rawItems.Count > 0;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _rawItems.AsEnumerable();

        // Period
        var now = DateTime.Now;
        filtered = SelectedPeriod switch
        {
            1 => filtered.Where(x => x.Timestamp.Date == now.Date),
            2 => filtered.Where(x => x.Timestamp >= now.AddDays(-7)),
            3 => filtered.Where(x => x.Timestamp >= now.AddDays(-30)),
            _ => filtered
        };

        // Action
        if (SelectedAction == 1)
            filtered = filtered.Where(x => x.ActionType == 1 || x.ActionType == 4);
        else if (SelectedAction != 0)
            filtered = filtered.Where(x => x.ActionType == SelectedAction);

        // Search
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim();
            filtered = filtered.Where(x =>
                (x.AnimeTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.RussianTitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = filtered.OrderByDescending(x => x.Timestamp).ToList();

        // Build groups by date, merging consecutive same-anime watch episodes.
        var newGroups = new List<HistoryGroup>();
        foreach (var dateGroup in list.GroupBy(x => x.Timestamp.Date).OrderByDescending(g => g.Key))
        {
            var group = new HistoryGroup { Header = GetFriendlyDate(dateGroup.Key) };
            HistoryEntryVm? run = null;
            foreach (var item in dateGroup) // already desc by timestamp
            {
                bool canMerge =
                    run != null &&
                    run.AnimeId == item.AnimeId &&
                    run.ActionType == item.ActionType &&
                    (item.ActionType == 1 || item.ActionType == 4 || item.ActionType == 6) &&
                    item.Episode > 0 &&
                    item.Episode == run.EpisodeFrom - 1;

                if (canMerge)
                {
                    run!.EpisodeFrom = item.Episode;
                    run.Count++;
                }
                else
                {
                    if (run != null) group.Items.Add(run);
                    run = new HistoryEntryVm
                    {
                        AnimeId = item.AnimeId,
                        AnimeTitle = item.AnimeTitle,
                        RussianTitle = item.RussianTitle,
                        PosterUrl = item.PosterUrl,
                        ActionType = item.ActionType,
                        Detail = item.Detail,
                        Timestamp = item.Timestamp,
                        EpisodeFrom = item.Episode,
                        EpisodeTo = item.Episode,
                        Primary = item
                    };
                }
            }
            if (run != null) group.Items.Add(run);
            if (group.Items.Count > 0) newGroups.Add(group);
        }

        GroupedHistory.Clear();
        foreach (var g in newGroups) GroupedHistory.Add(g);
        HasResults = newGroups.Count > 0;
    }

    [RelayCommand]
    public void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    public async Task OpenAnimeDetails(HistoryEntryVm entry)
    {
        if (entry == null) return;

        var fullItem = _animeService.Collection.FirstOrDefault(x => x.Id == entry.AnimeId);
        if (fullItem == null)
            fullItem = await _malApi.GetAnimeDetailsAsync(entry.AnimeId);

        if (fullItem != null)
        {
            await _dialogs.ShowAnimeDetailsAsync(null, fullItem);
            await RefreshHistory();
        }
    }

    private string GetFriendlyDate(DateTime date)
    {
        var now = DateTime.Now.Date;
        if (date == now) return UIUtils.GetLoc("common.time.today");
        if (date == now.AddDays(-1)) return UIUtils.GetLoc("common.time.yesterday");
        
        var culture = _settings.Current.UI.LanguageCode == Constants.Languages.Ru ? new System.Globalization.CultureInfo("ru-RU") : new System.Globalization.CultureInfo("en-US");
        return date.ToString("d MMMM", culture);
    }
}
