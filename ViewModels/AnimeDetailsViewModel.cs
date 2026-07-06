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

    [ObservableProperty]
    private string? _displayTargetType;

    public RelationItemVm(Models.Entities.AnimeRelation relation)
    {
        Relation = relation;
        DisplayTargetType = relation.TargetType;
    }
}

public partial class StaffWorkVm : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _score = string.Empty;

    [ObservableProperty]
    private Avalonia.Media.IBrush _highlightColor = Avalonia.Media.Brushes.Transparent;
}

public partial class StaffPlusItemVm : ObservableObject
{
    public Models.Entities.AnimeStaff Staff { get; }
    
    [ObservableProperty]
    private string _role = string.Empty;

    public System.Collections.ObjectModel.ObservableCollection<StaffWorkVm> BestWorks { get; } = new();

    public StaffPlusItemVm(Models.Entities.AnimeStaff staff)
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
    
    public System.Collections.ObjectModel.ObservableCollection<StaffPlusItemVm> StaffPlus { get; } = new();

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
            
            OnPropertyChanged(nameof(HasChanges));
            SaveCommand.NotifyCanExecuteChanged();
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
            var relations = await _jikanApiService.GetRelationsAsync(Anime.Id, Anime.MediaKind);
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
            var staffList = await _jikanApiService.GetStaffAsync(Anime.Id, Anime.MediaKind);
            _ = ProcessStaffPlusAsync(staffList);
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch staff for {Id}", Anime.Id);
        }
    }

    private async Task ProcessStaffPlusAsync(System.Collections.Generic.List<Models.Entities.AnimeStaff> staffList)
    {
        var keyRoles = new[] { "Original Creator", "Director", "Series Composition", "Script", "Music", "Character Design" };
        var staffPlusVms = new System.Collections.Generic.List<StaffPlusItemVm>();

        foreach (var s in staffList)
        {
            if (string.IsNullOrEmpty(s.Positions)) continue;
            var roles = s.Positions.Split(',').Select(r => r.Trim()).ToList();
            var matchedRole = roles.FirstOrDefault(r => keyRoles.Contains(r));
            if (matchedRole == null) continue;

            if (staffPlusVms.Count >= 10) break;
            if (s.PersonMalId == 0) continue;

            var personData = await _shikiApiService.GetPersonWorksAsync(s.PersonMalId);
            if (personData?.Works != null)
            {
                var vm = new StaffPlusItemVm(s) { Role = matchedRole };

                var validWorks = personData.Works
                    .Where(w => w.Anime != null && w.Anime.Id != Anime.Id)
                    .Where(w => w.Role != null && IsRoleMatch(w.Role, matchedRole))
                    .Select(w => new { Work = w, Score = double.TryParse(w.Anime!.Score, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var score) ? score : 0 })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                if (validWorks.Count > 0)
                {
                    foreach (var w in validWorks)
                    {
                        string scoreDisplay = w.Score > 0 ? w.Work.Anime!.Score! : "-";
                        
                        var localAnime = _animeService.Collection.FirstOrDefault(a => a.Id == w.Work.Anime!.Id);
                        Avalonia.Media.IBrush highlight = Avalonia.Media.Brushes.Transparent;
                        if (localAnime != null)
                        {
                            if (localAnime.Status == Models.Entities.UserAnimeStatus.Watching || localAnime.Status == Models.Entities.UserAnimeStatus.Completed)
                                highlight = Avalonia.Media.SolidColorBrush.Parse("#334CAF50");
                            else if (localAnime.Status == Models.Entities.UserAnimeStatus.Dropped)
                                highlight = Avalonia.Media.SolidColorBrush.Parse("#33F44336");
                        }

                        vm.BestWorks.Add(new StaffWorkVm 
                        { 
                            Title = string.IsNullOrEmpty(w.Work.Anime!.Russian) ? (w.Work.Anime!.Name ?? "Unknown") : w.Work.Anime.Russian,
                            Url = "https://shikimori.one" + w.Work.Anime.Url, 
                            Score = scoreDisplay,
                            HighlightColor = highlight
                        });
                    }
                    staffPlusVms.Add(vm);
                }
            }
        }

        var sorted = staffPlusVms.OrderByDescending(x => x.Role == "Original Creator").ThenBy(x => x.Role).ToList();

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StaffPlus.Clear();
            foreach (var vm in sorted)
            {
                StaffPlus.Add(vm);
            }
        });
    }

    private bool IsRoleMatch(string role, string matchedRole)
    {
        var r = role.ToLowerInvariant();
        return matchedRole switch
        {
            "Original Creator" => r.Contains("оригинал") || r.Contains("сюжет") || r.Contains("creator"),
            "Director" => (r.Contains("режисс") || r.Contains("director")) &&
                          !r.Contains("звук") && !r.Contains("sound") &&
                          !r.Contains("эпизод") && !r.Contains("episode") &&
                          !r.Contains("анимаци") && !r.Contains("animation") &&
                          !r.Contains("cg") && !r.Contains("3d") &&
                          !r.Contains("ассистент") && !r.Contains("assistant") &&
                          !r.Contains("помощник") && !r.Contains("второй") && !r.Contains("co-director"),
            "Series Composition" => r.Contains("компоновка") || r.Contains("структура") || r.Contains("series composition"),
            "Script" => r.Contains("сценар") || r.Contains("script"),
            "Music" => r.Contains("композитор") || r.Contains("музык") || r.Contains("music"),
            "Character Design" => r.Contains("дизайн персонажей") || r.Contains("character design"),
            _ => r.Contains(matchedRole.ToLowerInvariant())
        };
    }

    private async Task FetchRelationImageAsync(RelationItemVm vm)
    {
        var type = vm.Relation.TargetType?.ToLowerInvariant() ?? "";
        bool isAnime = type == "anime" || type == "tv" || type == "movie" || type == "ova" || type == "ona" || type == "special";

        var existing = _animeService.Collection.FirstOrDefault(x => x.Id == vm.Relation.TargetMalId && (isAnime ? x.MediaKind == MediaKind.Anime : x.MediaKind != MediaKind.Anime));
        if (existing != null && !string.IsNullOrEmpty(existing.MainPictureUrl))
        {
            vm.ImageUrl = existing.MainPictureUrl;
            if (!string.IsNullOrEmpty(existing.Type))
            {
                vm.DisplayTargetType = FormatMediaType(existing.Type);
            }
            return;
        }

        try
        {
            AnimeItem? details = null;
            if (isAnime)
            {
                details = await _malApiService.GetAnimeDetailsAsync(vm.Relation.TargetMalId);
            }
            else
            {
                details = await _malApiService.GetMangaDetailsAsync(vm.Relation.TargetMalId);
            }

            if (details != null)
            {
                if (!string.IsNullOrEmpty(details.MainPictureUrl))
                {
                    vm.ImageUrl = details.MainPictureUrl;
                }
                if (!string.IsNullOrEmpty(details.Type))
                {
                    vm.DisplayTargetType = FormatMediaType(details.Type);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch image for relation {TargetMalId}", vm.Relation.TargetMalId);
        }
    }

    private static string FormatMediaType(string type)
    {
        if (string.IsNullOrEmpty(type)) return "Unknown";
        var t = type.ToLowerInvariant();
        return t switch
        {
            "light_novel" => "Light Novel",
            "novel" => "Novel",
            "one_shot" => "One-shot",
            "doujinshi" => "Doujinshi",
            "manhwa" => "Manhwa",
            "manhua" => "Manhua",
            "oel" => "OEL",
            "manga" => "Manga",
            "tv" => "TV",
            "movie" => "Movie",
            "ova" => "OVA",
            "ona" => "ONA",
            "special" => "Special",
            "music" => "Music",
            _ => char.ToUpper(t[0]) + t.Substring(1)
        };
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
        string baseUrl = Kiriha.Core.ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
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
        string baseUrl = ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
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

    public bool HasChanges
    {
        get
        {
            if (_originalAnime == null || Anime == null) return false;
            
            string currentScore = Anime.Score ?? "";
            if (currentScore != "-" && currentScore.Contains(" "))
                currentScore = currentScore.Split(' ')[0];
                
            string origScore = _originalAnime.Score ?? "";
            if (origScore != "-" && origScore.Contains(" "))
                origScore = origScore.Split(' ')[0];

            return _originalAnime.Status != Anime.Status ||
                   _originalAnime.Progress != Anime.Progress ||
                   _originalAnime.ChaptersRead != Anime.ChaptersRead ||
                   _originalAnime.VolumesRead != Anime.VolumesRead ||
                   origScore != currentScore ||
                   _originalAnime.IsRewatching != Anime.IsRewatching ||
                   _originalAnime.RewatchCount != Anime.RewatchCount ||
                   _originalAnime.Notes != Anime.Notes ||
                   _originalAnime.DateStarted != Anime.DateStarted ||
                   _originalAnime.DateCompleted != Anime.DateCompleted;
        }
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
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
        if (relation == null || string.IsNullOrEmpty(relation.TargetType)) return;

        var type = relation.TargetType.ToLowerInvariant();
        MediaKind kind;

        if (type == "manga" || type == "manhwa" || type == "manhua" || type == "novel" || type == "light novel" || type == "one-shot" || type == "doujinshi" || type == "light_novel")
        {
            kind = type.Contains("novel") ? MediaKind.LightNovel : MediaKind.Manga;
        }
        else if (type == "anime" || type == "tv" || type == "movie" || type == "ova" || type == "ona" || type == "special")
        {
            kind = MediaKind.Anime;
        }
        else
        {
            kind = MediaKind.Anime;
        }

        var targetAnime = new AnimeItem
        {
            Id = relation.TargetMalId,
            Title = relation.TargetName,
            MediaKind = kind
        };

        // If the item exists in the collection, use the full one to ensure all offline fields are loaded.
        var existing = _animeService.Collection.FirstOrDefault(x => x.Id == targetAnime.Id && x.MediaKind == targetAnime.MediaKind);
        await _dialogs.ShowAnimeDetailsAsync(null, existing ?? targetAnime);
    }

    [RelayCommand]
    private void ShowFranchiseGraph()
    {
        var vm = new FranchiseGraphViewModel(Anime.Id, _shikiApiService, _dialogs);
        var window = new Kiriha.Views.FranchiseGraphWindow
        {
            DataContext = vm
        };
        
        // Show as a non-modal window or modal, depending on preference. Non-modal is better so user can keep it open.
        window.Show();
    }
}
