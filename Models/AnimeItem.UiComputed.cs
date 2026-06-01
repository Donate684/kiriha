using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kiriha.Models;

public partial class AnimeItem
{
    private string _fallbackSeason = string.Empty;

    [NotMapped]
    [JsonIgnore]
    public AnimeItemPresentation Presentation => new(this);

    [NotMapped]
    [JsonIgnore]
    public string DisplayTitle => Presentation.DisplayTitle;

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

    [NotMapped]
    [JsonIgnore]
    public string? DisplaySynopsis => Presentation.DisplaySynopsis;

    [JsonIgnore]
    public double ProgressValue => Presentation.ProgressValue;

    [JsonIgnore]
    public double ProgressValueFraction => Presentation.ProgressValueFraction;

    [JsonIgnore]
    public double AiredValueFraction => Presentation.AiredValueFraction;

    [JsonIgnore]
    public bool ShowAiredProgressBar => Presentation.ShowAiredProgressBar;

    [JsonIgnore]
    public string ProgressDisplay => Presentation.ProgressDisplay;

    [JsonIgnore]
    public bool CanEditProgress => Presentation.CanEditProgress;

    [JsonIgnore]
    public bool IsCompleted => Presentation.IsCompleted;

    [JsonIgnore]
    public bool ShowProgress => Presentation.ShowProgress;

    [JsonIgnore]
    public bool HasNewEpisodes => Presentation.HasNewEpisodes;

    [JsonIgnore]
    public int UnseenEpisodesCount => Presentation.UnseenEpisodesCount;

    [JsonIgnore]
    public bool IsNewEpisode => Presentation.IsNewEpisode;

    [JsonIgnore]
    public string AiringBadgeText => Presentation.AiringBadgeText;

    [JsonIgnore]
    public string NextEpisodeAtDisplay => Presentation.NextEpisodeAtDisplay;

    [JsonIgnore]
    public string AiringBadgeColor => Presentation.AiringBadgeColor;

    [JsonIgnore]
    public string ProgressPart => Presentation.ProgressPart;

    [JsonIgnore]
    public string TotalPart => Presentation.TotalPart;

    [JsonIgnore]
    public string DisplayAiringStatus => Presentation.DisplayAiringStatus;

    [JsonIgnore]
    public bool ShowAiredInfo => Presentation.ShowAiredInfo;

    [JsonIgnore]
    public bool HasGenres => Presentation.HasGenres;

    [NotMapped]
    [JsonIgnore]
    public IEnumerable<string> TopGenres => Presentation.TopGenres;

    [JsonIgnore]
    public bool HasStudios => Presentation.HasStudios;
}
