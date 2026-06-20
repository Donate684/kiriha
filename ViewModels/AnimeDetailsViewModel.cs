using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils;

namespace Kiriha.ViewModels;

public partial class AnimeDetailsViewModel : ViewModelBase
{
    [ObservableProperty]
    private AnimeItem _anime;

    private readonly AnimeItem _originalAnime;



    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<AnimeOfflineItem> _relatedAnime = new();

    /// <summary>
    /// User-defined share buttons resolved against the current anime. Rebuilt
    /// on construction; a settings change while this window is open does NOT
    /// live-refresh (the window is short-lived; reopening picks up changes).
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private readonly SettingsService _settingsService;
    private readonly MalApiService _malApiService;
    private readonly ShikiApiService _shikiApiService;
    private readonly SyncManager _syncManager;
    private readonly AnimeService _animeService;
    private readonly AiringInfoService _airingInfoService;
    private readonly HistoryService _historyService;

    public SettingsService Settings => _settingsService;

    public string JoinedGenres => string.Join(", ", Anime.Genres);
    public string JoinedStudios => string.Join(", ", Anime.Studios);
    public string JoinedAltTitles => string.Join(", ", Anime.AlternativeTitles);

    [ObservableProperty]
    private bool _isDeleteConfirmationVisible;

    private bool _isRemoving;

    public bool IsInList => Anime.Status != UserAnimeStatus.None;

    public System.Collections.Generic.IEnumerable<string> AllAlternativeTitles
    {
        get
        {
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Anime.EnglishTitle) && Anime.EnglishTitle != Anime.Title) 
                list.Add(Anime.EnglishTitle);
            if (!string.IsNullOrEmpty(Anime.JapaneseTitle) && Anime.JapaneseTitle != Anime.Title) 
                list.Add(Anime.JapaneseTitle);
            
            foreach (var syn in Anime.AlternativeTitles)
            {
                if (syn != Anime.Title && !list.Contains(syn))
                    list.Add(syn);
            }
            return list;
        }
    }

    public bool HasAlternativeTitles => AllAlternativeTitles.Any();

    public System.Collections.Generic.IEnumerable<UserAnimeStatus> AvailableStatuses => new[]
    {
        UserAnimeStatus.Watching,
        UserAnimeStatus.Completed,
        UserAnimeStatus.OnHold,
        UserAnimeStatus.Dropped,
        UserAnimeStatus.PlanToWatch
    };

    public System.Collections.Generic.IEnumerable<RatingOption> AvailableScores => new[] 
    { 
        RatingHelper.GetRatingOption("-"), 
        RatingHelper.GetRatingOption("10"), 
        RatingHelper.GetRatingOption("9"), 
        RatingHelper.GetRatingOption("8"), 
        RatingHelper.GetRatingOption("7"), 
        RatingHelper.GetRatingOption("6"), 
        RatingHelper.GetRatingOption("5"), 
        RatingHelper.GetRatingOption("4"), 
        RatingHelper.GetRatingOption("3"), 
        RatingHelper.GetRatingOption("2"), 
        RatingHelper.GetRatingOption("1") 
    };
    
    public string CombinedAltTitles
    {
        get
        {
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Anime.RussianTitle)) list.Add(Anime.RussianTitle);
            if (!string.IsNullOrEmpty(Anime.EnglishTitle)) list.Add(Anime.EnglishTitle);
            if (!string.IsNullOrEmpty(Anime.JapaneseTitle)) list.Add(Anime.JapaneseTitle);
            list.AddRange(Anime.AlternativeTitles);
            return string.Join(", ", list.Distinct());
        }
    }

    public AnimeDetailsViewModel(
        AnimeItem anime,
        MalApiService malApiService,
        ShikiApiService shikiApiService,
        SyncManager syncManager,
        AnimeService animeService,
        AiringInfoService airingInfoService,
        SettingsService settingsService,
        HistoryService historyService)
    {
        _originalAnime = anime;
        _anime = anime.Clone();

        _malApiService = malApiService;
        _shikiApiService = shikiApiService;
        _syncManager = syncManager;
        _animeService = animeService;
        _airingInfoService = airingInfoService;
        _settingsService = settingsService;
        _historyService = historyService;
        
        Anime.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(Anime.Status))
                OnPropertyChanged(nameof(IsInList));
        };

        BuildCustomShareLinks();

        InitializationAsync().SafeFireAndForget("AnimeDetailsInitialization");
    }

    private void BuildCustomShareLinks()
    {
        CustomShareLinks.Clear();
        foreach (var link in _settingsService.Current.CustomLinks)
        {
            if (string.IsNullOrWhiteSpace(link.UrlTemplate)) continue;
            var url = Kiriha.Core.CustomLinkResolver.Resolve(link.UrlTemplate, Anime);
            CustomShareLinks.Add(new CustomShareLinkRuntime(link.Name, link.IconKind, url, link.IconPath));
        }
    }

    private async Task InitializationAsync()
    {
        // Refresh properties based on existing clone data immediately
        Anime.RefreshMetadata();
        OnPropertyChanged(nameof(JoinedGenres));
        OnPropertyChanged(nameof(JoinedStudios));
        OnPropertyChanged(nameof(JoinedAltTitles));
        OnPropertyChanged(nameof(HasAlternativeTitles));
        OnPropertyChanged(nameof(AllAlternativeTitles));

        // Only fetch if we are missing critical metadata (like synopsis or genres).
        // Otherwise, rely entirely on the data already passed to this window.
        if (Anime.Genres.Count == 0 || string.IsNullOrEmpty(Anime.Synopsis))
        {
            IsLoading = true;
            try
            {
                await LoadFullDetailsAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadFullDetailsAsync()
    {
        var full = await _malApiService.GetAnimeDetailsAsync(Anime.Id);
        if (full != null)
        {
            // Sync metadata and user list properties to our CLONED object
            if (full.Status != UserAnimeStatus.None) Anime.Status = full.Status;
            Anime.Progress = full.Progress;
            Anime.Score = full.Score;
            Anime.Notes = full.Notes;
            Anime.RewatchCount = full.RewatchCount;
            Anime.IsRewatching = full.IsRewatching;
            Anime.DateStarted = full.DateStarted;
            Anime.DateCompleted = full.DateCompleted;
            
            // Metadata - overwrite only if we don't have better data or if specifically needed
            if (!string.IsNullOrEmpty(full.Synopsis)) Anime.Synopsis = full.Synopsis;
            
            if (full.Genres.Count > 0)
            {
                Anime.Genres.Clear();
                foreach(var g in full.Genres) Anime.Genres.Add(g);
            }

            if (full.Studios.Count > 0)
            {
                Anime.Studios.Clear();
                foreach(var s in full.Studios) Anime.Studios.Add(s);
            }

            if (full.AlternativeTitles.Count > 0)
            {
                foreach(var t in full.AlternativeTitles) 
                {
                    if (!Anime.AlternativeTitles.Contains(t))
                        Anime.AlternativeTitles.Add(t);
                }
            }
            
            Anime.EnglishTitle = full.EnglishTitle;
            Anime.JapaneseTitle = full.JapaneseTitle;
            Anime.StatusDetailed = full.StatusDetailed;
            Anime.MeanScore = full.MeanScore;
            Anime.Popularity = full.Popularity;
            Anime.Rank = full.Rank;
            Anime.AiringDate = full.AiringDate;
            Anime.StartSeason = full.StartSeason;
            Anime.StartYear = full.StartYear;
            
            // Re-trigger Season display evaluation
            Anime.Season = full.Season;

            Anime.RefreshMetadata();
            OnPropertyChanged(nameof(JoinedGenres));
            OnPropertyChanged(nameof(JoinedStudios));
            OnPropertyChanged(nameof(JoinedAltTitles));
            OnPropertyChanged(nameof(HasAlternativeTitles));
            OnPropertyChanged(nameof(AllAlternativeTitles));
        }
    }



    [RelayCommand]
    private async Task CopyMalLink()
    {
        string url = $"https://myanimelist.net/anime/{Anime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        string url = $"{Kiriha.Core.ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{Anime.Id}";
        await CopyToClipboard(url);
    }

    private void OpenInBrowser(string url)
    {
        ShellLauncher.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        OpenInBrowser($"https://myanimelist.net/anime/{Anime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        OpenInBrowser($"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{Anime.Id}");
    }

    [RelayCommand]
    private void IncrementProgress()
    {
        if (Anime.Progress < Anime.TotalEpisodes || Anime.TotalEpisodes == 0)
        {
            Anime.Progress++;
        }
    }

    [RelayCommand]
    private void SetStartDateToToday()
    {
        Anime.DateStarted = System.DateTime.Now;
    }

    [RelayCommand]
    private void SetEndDateToToday()
    {
        Anime.DateCompleted = System.DateTime.Now;
    }

    [RelayCommand]
    private void AddToList()
    {
        Anime.Status = UserAnimeStatus.Watching;
        // The property change notification for IsInList is handled by the ctor event handler
    }

    [RelayCommand]
    private async Task Save(object? window)
    {
        if (_isRemoving) return;

        bool markedAsDropped = _originalAnime.Status != UserAnimeStatus.Dropped && Anime.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = _originalAnime.Status != UserAnimeStatus.Completed && Anime.Status == UserAnimeStatus.Completed;

        // If the score ends with text (like "10 (Masterpiece)"), parse only the number
        string rawScore = Anime.Score;
        if (rawScore != "-" && rawScore.Contains(" "))
        {
            Anime.Score = rawScore.Split(' ')[0];
        }

        bool scoreChanged = _originalAnime.Score != Anime.Score && Anime.Score != "-" && !string.IsNullOrEmpty(Anime.Score);

        bool hasChanges = _originalAnime.Status != Anime.Status ||
                          _originalAnime.Progress != Anime.Progress ||
                          _originalAnime.Score != Anime.Score ||
                          _originalAnime.IsRewatching != Anime.IsRewatching ||
                          _originalAnime.RewatchCount != Anime.RewatchCount ||
                          _originalAnime.Notes != Anime.Notes ||
                          _originalAnime.DateStarted != Anime.DateStarted ||
                          _originalAnime.DateCompleted != Anime.DateCompleted;

        // Apply changes from clone to original item
        Anime.CopyTo(_originalAnime);

        if (_originalAnime.Status != UserAnimeStatus.None)
        {
            // Update local collection (add if new, update if exists)
            await _animeService.AddOrUpdateAnimeAsync(_originalAnime);

            if (markedAsDropped)
            {
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "Dropped");
            }
            if (markedAsCompleted)
            {
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "Completed");
            }
            if (scoreChanged)
            {
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "ScoreSet", _originalAnime.Score);
            }

            if (hasChanges)
            {
                // BACKGROUND SYNC: Enqueue the full item update
                await _syncManager.EnqueueFullUpdateAsync(_originalAnime);
            }

            // Notify UI to refresh lists
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }
        
        // Optimistically close window
        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    [RelayCommand]
    public async Task RemoveFromList(object window)
    {
        if (!IsDeleteConfirmationVisible)
        {
            IsDeleteConfirmationVisible = true;
            return;
        }

        _isRemoving = true;
        await _animeService.RemoveAnimeAsync(_originalAnime.Id);
        
        // Notify UI to refresh lists
        WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        
        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    private async Task CopyToClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }
}
