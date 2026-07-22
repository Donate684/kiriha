using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Kiriha.Utils.Async;
using Serilog;

namespace Kiriha.ViewModels.AnimeDetails;


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
    private readonly AnimeRepository _animeRepo;
    private readonly AnimeProgressService _animeProgressService;
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
        AnimeRepository animeRepo,
        AnimeProgressService animeProgressService,
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
        _animeRepo = animeRepo;
        _animeProgressService = animeProgressService;
        _airingInfoService = airingInfoService;
        _settingsService = settingsService;
        _historyService = historyService;
        _dialogs = dialogs;

        Anime.PropertyChanged += (s, e) =>
        {
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
                foreach (var g in full.Genres) Anime.Genres.Add(g);
            }

            if (full.Studios.Count > 0)
            {
                Anime.Studios.Clear();
                foreach (var s in full.Studios) Anime.Studios.Add(s);
            }

            if (full.AlternativeTitles.Count > 0)
            {
                foreach (var t in full.AlternativeTitles)
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




}
