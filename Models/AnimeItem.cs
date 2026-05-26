using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kiriha.Core;
using Kiriha.Models.Entities;

namespace Kiriha.Models;

public partial class AnimeItem : ObservableObject
{
    public int Id { get; set; }

    /// <summary>
    /// Priority of the source for EpisodesAired.
    /// 0 = None, 1 = Estimated (Math), 2 = Official (MAL/Shiki), 3 = Torrents (RSS/Search)
    /// </summary>
    public int AiredSourcePriority { get; set; }

    public string Title { get; set; } = string.Empty;

    private string? _russianTitle;
    public string? RussianTitle
    {
        get => _russianTitle;
        set 
        {
            if (SetProperty(ref _russianTitle, value))
                OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    [NotMapped]
    [JsonIgnore]
    public string DisplayTitle => !string.IsNullOrEmpty(RussianTitle) ? RussianTitle : Title;

    private bool _isHiddenInSeasons;
    /// <summary>
    /// Client-only flag mirrored from <c>AppSettings.UI.HiddenSeasonalIds</c>.
    /// Used purely by the Seasonal view to swap the hide/un-hide button icon.
    /// Not persisted - recomputed by <c>SeasonalViewModel.ApplyFiltersAsync</c>.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsHiddenInSeasons
    {
        get => _isHiddenInSeasons;
        set => SetProperty(ref _isHiddenInSeasons, value);
    }

    private bool _isHideConfirming;
    /// <summary>
    /// Transient UI state for the seasonal hide-button two-step confirmation.
    /// First click expands the pill into "Hide?" / "Скрыть?"; second click commits.
    /// Auto-resets on timeout / pointer leave / list reflow. Not persisted.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsHideConfirming
    {
        get => _isHideConfirming;
        set => SetProperty(ref _isHideConfirming, value);
    }

    private UserAnimeStatus _status = UserAnimeStatus.None;
    public UserAnimeStatus Status
    {
        get => _status;
        set 
        {
            if (SetProperty(ref _status, value))
                NotifyProgressChanges();
        }
    }

    private int _progress;
    public int Progress
    {
        get => _progress;
        set 
        {
            if (SetProperty(ref _progress, value))
                NotifyProgressChanges();
        }
    }

    private int _totalEpisodes;
    public int TotalEpisodes
    {
        get => _totalEpisodes;
        set 
        {
            if (SetProperty(ref _totalEpisodes, value))
                NotifyProgressChanges();
        }
    }

    /// <summary>
    /// ÃƒÂÃ‚Â­Ãƒâ€˜Ã¢â‚¬Å¾Ãƒâ€˜Ã¢â‚¬Å¾ÃƒÂÃ‚ÂµÃƒÂÃ‚ÂºÃƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â²ÃƒÂÃ‚Â½Ãƒâ€˜Ã¢â‚¬Â¹ÃƒÂÃ‚Â¹ ÃƒÂÃ‚Â·ÃƒÂÃ‚Â½ÃƒÂÃ‚Â°ÃƒÂÃ‚Â¼ÃƒÂÃ‚ÂµÃƒÂÃ‚Â½ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚ÂµÃƒÂÃ‚Â»Ãƒâ€˜Ã…â€™ ÃƒÂÃ‚Â´ÃƒÂÃ‚Â»Ãƒâ€˜Ã‚Â ÃƒÂÃ‚Â¿Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â³Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚ÂµÃƒâ€˜Ã‚ÂÃƒâ€˜Ã‚Â-ÃƒÂÃ‚Â±ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â°. ÃƒÂÃ¢â‚¬Â¢Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â»ÃƒÂÃ‚Â¸ TotalEpisodes ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â·ÃƒÂÃ‚Â²ÃƒÂÃ‚ÂµÃƒâ€˜Ã‚ÂÃƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚ÂµÃƒÂÃ‚Â½ ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â
    /// ÃƒÂÃ‚Â±ÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â€šÂ¬Ãƒâ€˜Ã¢â‚¬ËœÃƒÂÃ‚Â¼ ÃƒÂÃ‚ÂµÃƒÂÃ‚Â³ÃƒÂÃ‚Â¾; ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â½ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â‚¬Â¡ÃƒÂÃ‚Âµ ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Ãƒâ€šÃ‚Â«ÃƒÂÃ‚Â±ÃƒÂÃ‚Â°ÃƒÂÃ‚ÂºÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â» (12/24/...) ÃƒÂÃ‚Â½ÃƒÂÃ‚Â° ÃƒÂÃ‚Â¾Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â½ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â²ÃƒÂÃ‚Âµ max(Progress, EpisodesAired),
    /// Ãƒâ€˜Ã¢â‚¬Â¡Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â±Ãƒâ€˜Ã¢â‚¬Â¹ ÃƒÂÃ‚ÂºÃƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â½ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚Â (aired) ÃƒÂÃ‚Â¸ ÃƒÂÃ‚Â°ÃƒÂÃ‚ÂºÃƒâ€˜Ã¢â‚¬Â ÃƒÂÃ‚ÂµÃƒÂÃ‚Â½Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â½ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚Â (watched) ÃƒÂÃ‚Â¿ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â»ÃƒÂÃ‚Â¾Ãƒâ€˜Ã‚ÂÃƒÂÃ‚ÂºÃƒÂÃ‚Â¸ ÃƒÂÃ‚Â´ÃƒÂÃ‚ÂµÃƒÂÃ‚Â»ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â»ÃƒÂÃ‚Â¸Ãƒâ€˜Ã‚ÂÃƒâ€˜Ã…â€™ ÃƒÂÃ‚Â½ÃƒÂÃ‚Â° ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â´ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â½ ÃƒÂÃ‚Â·ÃƒÂÃ‚Â½ÃƒÂÃ‚Â°ÃƒÂÃ‚Â¼ÃƒÂÃ‚ÂµÃƒÂÃ‚Â½ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚ÂµÃƒÂÃ‚Â»Ãƒâ€˜Ã…â€™.
    /// </summary>
    [JsonIgnore]
    private int EffectiveTotal
    {
        get
        {
            if (TotalEpisodes > 0) return TotalEpisodes;
            int m = Math.Max(Progress, EpisodesAired);
            if (m <= 0) return 0;
            return m > 24 ? ((m - 1) / 12 + 1) * 12 : (m > 12 ? 24 : 12);
        }
    }

    [JsonIgnore]
    public double ProgressValue
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            return Math.Clamp((double)Progress / total * 100, 0, 100);
        }
    }

    [JsonIgnore]
    public double ProgressValueFraction => ProgressValue / 100.0;

    /// <summary>
    /// ÃƒÂÃ¢â‚¬ÂÃƒÂÃ‚Â¾ÃƒÂÃ‚Â»Ãƒâ€˜Ã‚Â Ãƒâ€šÃ‚Â«ÃƒÂÃ‚Â²Ãƒâ€˜Ã¢â‚¬Â¹Ãƒâ€˜Ã‹â€ ÃƒÂÃ‚Â»ÃƒÂÃ‚Â¾ Ãƒâ€˜Ã‚ÂÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â¹Ãƒâ€šÃ‚Â» ÃƒÂÃ‚Â¾Ãƒâ€˜Ã¢â‚¬Å¡ ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â±Ãƒâ€˜Ã¢â‚¬Â°ÃƒÂÃ‚ÂµÃƒÂÃ‚Â³ÃƒÂÃ‚Â¾ Ãƒâ€˜Ã¢â‚¬Â¡ÃƒÂÃ‚Â¸Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â»ÃƒÂÃ‚Â° Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â¿ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â·ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â´ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â² ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â ÃƒÂÃ‚Â¸Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â¿ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â»Ãƒâ€˜Ã…â€™ÃƒÂÃ‚Â·Ãƒâ€˜Ã†â€™ÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã‚ÂÃƒâ€˜Ã‚Â ÃƒÂÃ‚Â´ÃƒÂÃ‚Â»Ãƒâ€˜Ã‚Â ÃƒÂÃ‚ÂºÃƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â½ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â¹
    /// Ãƒâ€˜Ã¢â‚¬Â¡ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚ÂÃƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â¸ ÃƒÂÃ‚Â¿Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â³Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚ÂµÃƒâ€˜Ã‚ÂÃƒâ€˜Ã‚Â-ÃƒÂÃ‚Â±ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â° (Ãƒâ€˜Ã‚ÂÃƒÂÃ‚ÂºÃƒÂÃ‚Â¾ÃƒÂÃ‚Â»Ãƒâ€˜Ã…â€™ÃƒÂÃ‚ÂºÃƒÂÃ‚Â¾ Ãƒâ€˜Ã†â€™ÃƒÂÃ‚Â¶ÃƒÂÃ‚Âµ ÃƒÂÃ‚Â²Ãƒâ€˜Ã¢â‚¬Â¹Ãƒâ€˜Ã‹â€ ÃƒÂÃ‚Â»ÃƒÂÃ‚Â¾, ÃƒÂÃ‚Â½ÃƒÂÃ‚Â¾ ÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â‚¬Â°Ãƒâ€˜Ã¢â‚¬Ëœ ÃƒÂÃ‚Â½ÃƒÂÃ‚Âµ ÃƒÂÃ‚Â¿Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â¾Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â¼ÃƒÂÃ‚Â¾Ãƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚ÂµÃƒÂÃ‚Â½ÃƒÂÃ‚Â¾).
    /// </summary>
    [JsonIgnore]
    public double AiredValueFraction
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            var aired = StatusDetailed == "finished_airing" && TotalEpisodes > 0
                ? TotalEpisodes
                : EpisodesAired;
            if (aired <= 0) return 0;
            return Math.Clamp((double)aired / total, 0, 1);
        }
    }

    /// <summary>
    /// ÃƒÂÃ…Â¸ÃƒÂÃ‚Â¾ÃƒÂÃ‚ÂºÃƒÂÃ‚Â°ÃƒÂÃ‚Â·Ãƒâ€˜Ã¢â‚¬Â¹ÃƒÂÃ‚Â²ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã…â€™ ÃƒÂÃ‚Â»ÃƒÂÃ‚Â¸ ÃƒÂÃ‚ÂºÃƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â½Ãƒâ€˜Ã†â€™Ãƒâ€˜Ã…Â½ ÃƒÂÃ‚Â¿ÃƒÂÃ‚Â¾ÃƒÂÃ‚Â»ÃƒÂÃ‚Â¾Ãƒâ€˜Ã‚ÂÃƒÂÃ‚ÂºÃƒâ€˜Ã†â€™ (ÃƒÂÃ‚ÂµÃƒâ€˜Ã‚ÂÃƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã…â€™ ÃƒÂÃ‚Â½ÃƒÂÃ‚ÂµÃƒÂÃ‚Â¿Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â¾Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Â¼ÃƒÂÃ‚Â¾Ãƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚ÂµÃƒÂÃ‚Â½ÃƒÂÃ‚Â½Ãƒâ€˜Ã¢â‚¬Â¹ÃƒÂÃ‚Âµ Ãƒâ€˜Ã†â€™ÃƒÂÃ‚Â¶ÃƒÂÃ‚Âµ ÃƒÂÃ‚Â²Ãƒâ€˜Ã¢â‚¬Â¹Ãƒâ€˜Ã‹â€ ÃƒÂÃ‚ÂµÃƒÂÃ‚Â´Ãƒâ€˜Ã‹â€ ÃƒÂÃ‚Â¸ÃƒÂÃ‚Âµ Ãƒâ€˜Ã‚ÂÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â€šÂ¬ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â¸
    /// Ãƒâ€˜Ã†â€™ ÃƒÂÃ‚Â°ÃƒÂÃ‚Â½ÃƒÂÃ‚Â¸ÃƒÂÃ‚Â¼ÃƒÂÃ‚Âµ ÃƒÂÃ‚Â² Ãƒâ€˜Ã‚ÂÃƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â°Ãƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã†â€™Ãƒâ€˜Ã‚ÂÃƒÂÃ‚Âµ Ãƒâ€šÃ‚Â«ÃƒÂÃ‚Â¡ÃƒÂÃ‚Â¼ÃƒÂÃ‚Â¾Ãƒâ€˜Ã¢â‚¬Å¡Ãƒâ€˜Ã¢â€šÂ¬Ãƒâ€˜Ã…Â½Ãƒâ€šÃ‚Â», ÃƒÂÃ‚Â²ÃƒÂÃ‚ÂºÃƒÂÃ‚Â»Ãƒâ€˜Ã…Â½Ãƒâ€˜Ã¢â‚¬Â¡ÃƒÂÃ‚Â°Ãƒâ€˜Ã‚Â ÃƒÂÃ‚Â·ÃƒÂÃ‚Â°ÃƒÂÃ‚Â²ÃƒÂÃ‚ÂµÃƒâ€˜Ã¢â€šÂ¬Ãƒâ€˜Ã‹â€ Ãƒâ€˜Ã¢â‚¬ËœÃƒÂÃ‚Â½ÃƒÂÃ‚Â½Ãƒâ€˜Ã¢â‚¬Â¹ÃƒÂÃ‚Âµ Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â°ÃƒÂÃ‚Â¹Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â»Ãƒâ€˜Ã¢â‚¬Â¹ ÃƒÂÃ‚Â¸ Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â°ÃƒÂÃ‚Â¹Ãƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â»Ãƒâ€˜Ã¢â‚¬Â¹ Ãƒâ€˜Ã‚Â
    /// ÃƒÂÃ‚Â½ÃƒÂÃ‚ÂµÃƒÂÃ‚Â¸ÃƒÂÃ‚Â·ÃƒÂÃ‚Â²ÃƒÂÃ‚ÂµÃƒâ€˜Ã‚ÂÃƒâ€˜Ã¢â‚¬Å¡ÃƒÂÃ‚Â½Ãƒâ€˜Ã¢â‚¬Â¹ÃƒÂÃ‚Â¼ TotalEpisodes).
    /// </summary>
    [JsonIgnore]
    public bool ShowAiredProgressBar
    {
        get
        {
            if (Status != UserAnimeStatus.Watching || IsCompleted) return false;
            var aired = StatusDetailed == "finished_airing" && TotalEpisodes > 0
                ? TotalEpisodes
                : EpisodesAired;
            if (aired <= 0) return false;
            return Progress < aired;
        }
    }

    private string _score = "-";
    public string Score
    {
        get => _score;
        set => SetProperty(ref _score, value);
    }

    private string _type = Constants.AnimeTypes.Tv;
    public string Type 
    { 
        get => _type;
        set => SetProperty(ref _type, value);
    }
    
    private string _fallbackSeason = string.Empty;

    [NotMapped]
    public string Season 
    { 
        get
        {
            if (!string.IsNullOrEmpty(StartSeason) && StartYear.HasValue)
                return $"{StartSeason} {StartYear}";
            if (!string.IsNullOrEmpty(StartSeason))
                return StartSeason;
            if (StartYear.HasValue)
                return StartYear.ToString()!;
            return _fallbackSeason;
        }
        set 
        { 
            _fallbackSeason = value ?? string.Empty;
            OnPropertyChanged(nameof(Season));
        }
    }

    [JsonIgnore]
    public string ProgressDisplay
    {
        get
        {
             if (IsCompleted && !IsRewatching) return TotalPart;
             return $"{ProgressPart} {TotalPart}";
        }
    }

    [JsonIgnore]
    public bool CanEditProgress => !IsCompleted || IsRewatching;

    [JsonIgnore]
    public bool IsCompleted => Status == UserAnimeStatus.Completed;

    [JsonIgnore]
    public bool ShowProgress => !IsCompleted || IsRewatching;

    [JsonIgnore]
    public bool HasNewEpisodes => StatusDetailed == "currently_airing" && Status == UserAnimeStatus.Watching && Progress < EpisodesAired;

    [JsonIgnore]
    public int UnseenEpisodesCount
    {
        get
        {
            if (!ShowAiredProgressBar) return 0;
            var aired = StatusDetailed == "finished_airing" && TotalEpisodes > 0
                ? TotalEpisodes
                : EpisodesAired;
            return Math.Max(0, aired - Progress);
        }
    }

    private int _episodesAired;
    public int EpisodesAired
    {
        get => _episodesAired;
        set 
        {
            if (value == 0 && _episodesAired > 0) return;
            if (SetProperty(ref _episodesAired, value))
            {
                OnPropertyChanged(nameof(ProgressDisplay));
                OnPropertyChanged(nameof(HasNewEpisodes));
                OnPropertyChanged(nameof(UnseenEpisodesCount));
                OnPropertyChanged(nameof(ShowAiredProgressBar));
                OnPropertyChanged(nameof(AiredValueFraction));
            }
        }
    }

    private string? _synopsis = string.Empty;
    public string? Synopsis 
    { 
        get => _synopsis;
        set => SetProperty(ref _synopsis, value);
    }
    
    private string? _russianSynopsis = string.Empty;
    public string? RussianSynopsis
    {
        get => _russianSynopsis;
        set 
        {
            if (SetProperty(ref _russianSynopsis, value))
                OnPropertyChanged(nameof(DisplaySynopsis));
        }
    }

    [NotMapped]
    [JsonIgnore]
    public string? DisplaySynopsis => !string.IsNullOrEmpty(RussianSynopsis) ? RussianSynopsis : Synopsis;

    private string? _mainPictureUrl;
    public string? MainPictureUrl
    {
        get => _mainPictureUrl;
        set => SetProperty(ref _mainPictureUrl, value);
    }
    public string? LocalPosterPath { get; set; }
    public string? Nsfw { get; set; }
    public string? EnglishTitle { get; set; }
    public string? JapaneseTitle { get; set; }
    public List<string> AlternativeTitles { get; set; } = new();
    public List<string> Genres { get; set; } = new();
    public List<string> Studios { get; set; } = new();
    public string? StatusDetailed { get; set; }
    public string? MeanScore { get; set; }
    public int Popularity { get; set; }
    public int? Rank { get; set; }
    public DateTime? AiringDate { get; set; }
    public string? StartSeason { get; set; }
    private int? _startYear;
    public int? StartYear
    {
        get => _startYear;
        set 
        {
            if (SetProperty(ref _startYear, value))
                OnPropertyChanged(nameof(Season));
        }
    }
    public string? Rating { get; set; }
    public string? Notes { get; set; }
    public bool IsRewatching { get; set; }
    public int RewatchCount { get; set; }
    public DateTime? DateStarted { get; set; }
    public DateTime? DateCompleted { get; set; }
    public string? BroadcastDay { get; set; }
    public string? BroadcastTime { get; set; }
    public DateTime? LastEpisodeAt { get; set; }
    public DateTime? LastEpisodesSync { get; set; }

    [JsonIgnore]
    public bool IsNewEpisode => LastEpisodeAt.HasValue && (DateTime.Now - LastEpisodeAt.Value).TotalDays < 2;

    [JsonIgnore]
    public string AiringBadgeText
    {
        get
        {
            if (IsNewEpisode && HasNewEpisodes) return Kiriha.Core.UIUtils.GetLoc("anime.labels.new_ep");
            if (NextEpisodeAt.HasValue)
            {
                var diff = NextEpisodeAt.Value - DateTime.Now;

                // Show the unconfirmed "?" only within a 48h grace period after the expected
                // air time. Beyond that, the date is almost certainly stale (sources didn't
                // refresh) and the "?" would otherwise stay forever.
                if (diff.TotalSeconds <= 0)
                {
                    if (diff.TotalHours < -48) return string.Empty;
                    return Kiriha.Core.UIUtils.GetLoc("anime.labels.new_ep") + "?";
                }

                if (diff.TotalDays >= 1) 
                    return $"{(int)diff.TotalDays}{Kiriha.Core.UIUtils.GetLoc("common.time.day_abbr")}";
                
                if (diff.TotalHours >= 1)
                    return $"{diff.Hours}{Kiriha.Core.UIUtils.GetLoc("common.time.hour_abbr")} {diff.Minutes}{Kiriha.Core.UIUtils.GetLoc("common.time.min_abbr")}";
                
                return $"{diff.Minutes}{Kiriha.Core.UIUtils.GetLoc("common.time.min_abbr")}";
            }
            return string.Empty;
        }
    }

    private DateTime? _nextEpisodeAt;
    public DateTime? NextEpisodeAt
    {
        get => _nextEpisodeAt;
        set
        {
            if (SetProperty(ref _nextEpisodeAt, value))
            {
                OnPropertyChanged(nameof(AiringBadgeText));
                OnPropertyChanged(nameof(NextEpisodeAtDisplay));
                OnPropertyChanged(nameof(AiringBadgeColor));
            }
        }
    }

    [JsonIgnore]
    public string NextEpisodeAtDisplay => NextEpisodeAt?.ToString("g") ?? "-";

    [JsonIgnore]
    public string AiringBadgeColor
    {
        get
        {
            if (IsNewEpisode && HasNewEpisodes) return "#FF4500";
            if (NextEpisodeAt.HasValue && (NextEpisodeAt.Value - DateTime.Now).TotalSeconds <= 0) return "#FF8C00";
            return "#4CAF50";
        }
    }
    [JsonIgnore]
    public string ProgressPart => Progress.ToString();

    [JsonIgnore]
    public string TotalPart
    {
        get
        {
            var total = TotalEpisodes > 0 ? TotalEpisodes.ToString() : "?";
            if (IsCompleted && !IsRewatching)
                return UIUtils.GetLoc("anime.labels.total_ep_finished", total);
            return UIUtils.GetLoc("anime.labels.total_ep_format", total);
        }
    }

    [JsonIgnore]
    public string DisplayAiringStatus
    {
        get
        {
            return StatusDetailed switch
            {
                "currently_airing" => Kiriha.Core.UIUtils.GetLoc("anime.status.currently_airing"),
                "finished_airing" => Kiriha.Core.UIUtils.GetLoc("anime.status.finished_airing"),
                "not_yet_aired" => Kiriha.Core.UIUtils.GetLoc("anime.status.not_yet_aired"),
                _ => StatusDetailed != null ? Kiriha.Core.UIUtils.GetLoc("anime.status." + StatusDetailed) : Kiriha.Core.UIUtils.GetLoc("anime.status.unknown")
            };
        }
    }

    [JsonIgnore]
    public bool ShowAiredInfo => StatusDetailed != "finished_airing";

    private void NotifyProgressChanges()
    {
        OnPropertyChanged(nameof(ProgressDisplay));
        OnPropertyChanged(nameof(ProgressPart));
        OnPropertyChanged(nameof(TotalPart));
        OnPropertyChanged(nameof(HasNewEpisodes));
        OnPropertyChanged(nameof(UnseenEpisodesCount));
        OnPropertyChanged(nameof(ShowAiredProgressBar));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressValueFraction));
        OnPropertyChanged(nameof(AiredValueFraction));
        OnPropertyChanged(nameof(CanEditProgress));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(ShowProgress));
    }

    public AnimeItem Clone()
    {
        var clone = (AnimeItem)this.MemberwiseClone();
        clone.AlternativeTitles = new List<string>(AlternativeTitles);
        clone.Genres = new List<string>(Genres);
        clone.Studios = new List<string>(Studios);
        return clone;
    }

    public void CopyTo(AnimeItem target)
    {
        target.Title = Title;
        target.RussianTitle = RussianTitle;
        target.Status = Status;
        target.Progress = Progress;
        target.TotalEpisodes = TotalEpisodes;
        target.Score = Score;
        target.Type = Type;
        target.Synopsis = Synopsis;
        target.RussianSynopsis = RussianSynopsis;
        target.MainPictureUrl = MainPictureUrl;
        target.LocalPosterPath = LocalPosterPath;
        target.Nsfw = Nsfw;
        target.EnglishTitle = EnglishTitle;
        target.JapaneseTitle = JapaneseTitle;
        target.StatusDetailed = StatusDetailed;
        target.MeanScore = MeanScore;
        target.Popularity = Popularity;
        target.Rank = Rank;
        target.AiringDate = AiringDate;
        target.StartYear = StartYear;
        target.StartSeason = StartSeason;
        target.Rating = Rating;
        target.Notes = Notes;
        target.IsRewatching = IsRewatching;
        target.RewatchCount = RewatchCount;
        target.DateStarted = DateStarted;
        target.DateCompleted = DateCompleted;
        target.BroadcastDay = BroadcastDay;
        target.BroadcastTime = BroadcastTime;
        target.LastEpisodesSync = LastEpisodesSync;

        if (this.AiredSourcePriority >= target.AiredSourcePriority || this.EpisodesAired > target.EpisodesAired)
        {
            target.EpisodesAired = this.EpisodesAired;
            target.AiredSourcePriority = this.AiredSourcePriority;
            target.LastEpisodeAt = this.LastEpisodeAt;
        }

        if (this.NextEpisodeAt.HasValue) target.NextEpisodeAt = this.NextEpisodeAt;

        target.Genres = new List<string>(Genres);
        target.Studios = new List<string>(Studios);
        target.AlternativeTitles = new List<string>(AlternativeTitles);

        target.RefreshMetadata();
    }

    [JsonIgnore]
    public bool HasGenres => Genres != null && Genres.Count > 0;

    [NotMapped]
    [JsonIgnore]
    public IEnumerable<string> TopGenres => Genres?.Take(2) ?? Enumerable.Empty<string>();

    [JsonIgnore]
    public bool HasStudios => Studios != null && Studios.Count > 0;

    public void RefreshMetadata() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Lightweight refresh for the airing countdown - re-evaluates only the
    /// time-dependent properties (<see cref="AiringBadgeText"/>,
    /// <see cref="AiringBadgeColor"/>, <see cref="IsNewEpisode"/>) without
    /// broadcasting a full property reset like <see cref="RefreshMetadata"/>.
    /// Called by a per-minute ticker in the list VM so the "Hч Mм" pill
    /// counts down in real time.
    /// </summary>
    public void RefreshAiringBadge()
    {
        OnPropertyChanged(nameof(AiringBadgeText));
        OnPropertyChanged(nameof(AiringBadgeColor));
        OnPropertyChanged(nameof(IsNewEpisode));
    }
}
