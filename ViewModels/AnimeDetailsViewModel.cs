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
using Serilog;
using Kiriha.Core.Dialogs;

namespace Kiriha.ViewModels;

public partial class RelationItemVm : ObservableObject
{
    public Models.Entities.AnimeRelation Relation { get; }
    
    [ObservableProperty]
    private string? _imageUrl;

    public RelationItemVm(Models.Entities.AnimeRelation relation)
    {
        Relation = relation;
    }
}

public partial class StaffItemVm : ObservableObject
{
    public Models.Entities.AnimeStaff Staff { get; }

    public StaffItemVm(Models.Entities.AnimeStaff staff)
    {
        Staff = staff;
    }
}

public partial class AnimeDetailsViewModel : ViewModelBase
{
    [ObservableProperty]
    private AnimeItem _anime;

    private readonly AnimeItem _originalAnime;



    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    public System.Collections.ObjectModel.ObservableCollection<AnimeOfflineItem> _relatedAnime = new();

    public System.Collections.ObjectModel.ObservableCollection<RelationItemVm> Relations { get; } = new();
    
    public System.Collections.ObjectModel.ObservableCollection<StaffItemVm> Staff { get; } = new();

    /// <summary>
    /// User-defined share buttons resolved against the current anime. Rebuilt
    /// on construction; a settings change while this window is open does NOT
    /// live-refresh (the window is short-lived; reopening picks up changes).
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<CustomShareLinkRuntime> CustomShareLinks { get; } = new();

    private readonly SettingsService _settingsService;
    private readonly MalApiService _malApiService;
    private readonly ShikiApiService _shikiApiService;
    private readonly JikanApiService _jikanApiService;
    private readonly SyncManager _syncManager;
    private readonly AnimeService _animeService;
    private readonly AiringInfoService _airingInfoService;
    private readonly HistoryService _historyService;
    private readonly IDialogService _dialogs;

    public SettingsService Settings => _settingsService;

    public string JoinedGenres => string.Join(", ", Anime.Genres);
    public string JoinedStudios => string.Join(", ", Anime.Studios);
    public string JoinedAltTitles => string.Join(", ", Anime.AlternativeTitles);

    [ObservableProperty]
    private bool _isDeleteConfirmationVisible;

    private bool _isRemoving;

    public bool IsInList => Anime.Status != UserAnimeStatus.None;

    private System.Collections.Generic.IEnumerable<string>? _allAlternativeTitles;

    public System.Collections.Generic.IEnumerable<string> AllAlternativeTitles
    {
        get
        {
            if (_allAlternativeTitles != null)
                return _allAlternativeTitles;

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
            
            return _allAlternativeTitles = list;
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
    
    private string? _combinedAltTitles;

    public string CombinedAltTitles
    {
        get
        {
            if (_combinedAltTitles != null)
                return _combinedAltTitles;

            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(Anime.RussianTitle)) list.Add(Anime.RussianTitle);
            if (!string.IsNullOrEmpty(Anime.EnglishTitle)) list.Add(Anime.EnglishTitle);
            if (!string.IsNullOrEmpty(Anime.JapaneseTitle)) list.Add(Anime.JapaneseTitle);
            list.AddRange(Anime.AlternativeTitles);
            
            return _combinedAltTitles = string.Join(", ", list.Distinct());
        }
    }

    public AnimeDetailsViewModel(
        AnimeItem anime,
        MalApiService malApiService,
        ShikiApiService shikiApiService,
        JikanApiService jikanApiService,
        SyncManager syncManager,
        AnimeService animeService,
        AiringInfoService airingInfoService,
        SettingsService settingsService,
        HistoryService historyService,
        IDialogService dialogs)
    {
        _originalAnime = anime;
        _anime = anime.Clone();

        _malApiService = malApiService;
        _shikiApiService = shikiApiService;
        _jikanApiService = jikanApiService;
        _syncManager = syncManager;
        _animeService = animeService;
        _airingInfoService = airingInfoService;
        _settingsService = settingsService;
        _historyService = historyService;
        _dialogs = dialogs;
        
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
        _allAlternativeTitles = null;
        _combinedAltTitles = null;
        OnPropertyChanged(nameof(JoinedGenres));
        OnPropertyChanged(nameof(JoinedStudios));
        OnPropertyChanged(nameof(JoinedAltTitles));
        OnPropertyChanged(nameof(CombinedAltTitles));
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

        try
        {
            var relations = await _jikanApiService.GetRelationsAsync(Anime.Id);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Relations.Clear();
                foreach (var r in relations) 
                {
                    var vm = new RelationItemVm(r);
                    Relations.Add(vm);
                    _ = FetchRelationImageAsync(vm);
                }
            });
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch relations for {Id}", Anime.Id);
        }

        try
        {
            var staffList = await _jikanApiService.GetStaffAsync(Anime.Id);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Staff.Clear();
                foreach (var s in staffList)
                {
                    Staff.Add(new StaffItemVm(s));
                }
            });
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch staff for {Id}", Anime.Id);
        }
    }

    private async Task FetchRelationImageAsync(RelationItemVm vm)
    {
        var existing = _animeService.Collection.FirstOrDefault(x => x.Id == vm.Relation.TargetMalId);
        if (existing != null && !string.IsNullOrEmpty(existing.MainPictureUrl))
        {
            vm.ImageUrl = existing.MainPictureUrl;
            return;
        }

        try
        {
            var details = await _malApiService.GetAnimeDetailsAsync(vm.Relation.TargetMalId);
            if (details != null && !string.IsNullOrEmpty(details.MainPictureUrl))
            {
                vm.ImageUrl = details.MainPictureUrl;
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch image for relation {TargetMalId}", vm.Relation.TargetMalId);
        }
    }

    private async Task LoadFullDetailsAsync()
    {
        var full = Anime.IsManga 
            ? await _malApiService.GetMangaDetailsAsync(Anime.Id)
            : await _malApiService.GetAnimeDetailsAsync(Anime.Id);
            
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
            
            if (!string.IsNullOrEmpty(full.MainPictureUrl)) Anime.MainPictureUrl = full.MainPictureUrl;
            if (!string.IsNullOrEmpty(full.LocalPosterPath)) Anime.LocalPosterPath = full.LocalPosterPath;
            
            // Re-trigger Season display evaluation
            Anime.Season = full.Season;

            Anime.RefreshMetadata();
            _allAlternativeTitles = null;
            _combinedAltTitles = null;
            OnPropertyChanged(nameof(JoinedGenres));
            OnPropertyChanged(nameof(JoinedStudios));
            OnPropertyChanged(nameof(JoinedAltTitles));
            OnPropertyChanged(nameof(CombinedAltTitles));
            OnPropertyChanged(nameof(HasAlternativeTitles));
            OnPropertyChanged(nameof(AllAlternativeTitles));
        }
    }



    [RelayCommand]
    private async Task CopyMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        string url = $"https://myanimelist.net/{type}/{Anime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        string baseUrl = Kiriha.Core.ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror);
        if (Anime.MediaKind != MediaKind.Anime) baseUrl = baseUrl.Replace("/animes/", "/mangas/");
        string url = $"{baseUrl}{Anime.Id}";
        await CopyToClipboard(url);
    }

    private void OpenInBrowser(string url)
    {
        ShellLauncher.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        OpenInBrowser($"https://myanimelist.net/{type}/{Anime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        string baseUrl = ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror);
        if (Anime.MediaKind != MediaKind.Anime) baseUrl = baseUrl.Replace("/animes/", "/mangas/");
        OpenInBrowser($"{baseUrl}{Anime.Id}");
    }

    [RelayCommand]
    private void IncrementProgress()
    {
        if (Anime.MediaKind != MediaKind.Anime)
        {
            if (Anime.ChaptersRead < Anime.Chapters || Anime.Chapters == 0)
                Anime.ChaptersRead++;
        }
        else
        {
            if (Anime.Progress < Anime.TotalEpisodes || Anime.TotalEpisodes == 0)
                Anime.Progress++;
        }
    }

    [RelayCommand]
    private void IncrementVolumes()
    {
        if (Anime.VolumesRead < Anime.Volumes || Anime.Volumes == 0)
            Anime.VolumesRead++;
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
                          _originalAnime.ChaptersRead != Anime.ChaptersRead ||
                          _originalAnime.VolumesRead != Anime.VolumesRead ||
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

    [RelayCommand]
    private async Task NavigateToRelation(Models.Entities.AnimeRelation relation)
    {
        if (relation == null || relation.TargetType != "anime") return;

        var targetAnime = new AnimeItem
        {
            Id = relation.TargetMalId,
            Title = relation.TargetName
        };

        // If the item exists in the collection, use the full one to ensure all offline fields are loaded.
        var existing = _animeService.Collection.FirstOrDefault(x => x.Id == targetAnime.Id);
        await _dialogs.ShowAnimeDetailsAsync(null, existing ?? targetAnime);
    }
}
