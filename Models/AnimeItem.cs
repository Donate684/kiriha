using System;
using System.Collections.Generic;
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
                NotifyTitleChanged();
        }
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

    private int _episodesAired;
    public int EpisodesAired
    {
        get => _episodesAired;
        set
        {
            if (value == 0 && _episodesAired > 0) return;
            if (SetProperty(ref _episodesAired, value))
                NotifyEpisodesAiredChanged();
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
                NotifySynopsisChanged();
        }
    }

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
                NotifySeasonChanged();
        }
    }

    public string? Rating { get; set; }
    public string? Notes { get; set; }
    private bool _isRewatching;
    public bool IsRewatching
    {
        get => _isRewatching;
        set
        {
            if (SetProperty(ref _isRewatching, value))
                NotifyProgressChanges();
        }
    }
    public int RewatchCount { get; set; }
    public DateTime? DateStarted { get; set; }
    public DateTime? DateCompleted { get; set; }
    public string? BroadcastDay { get; set; }
    public string? BroadcastTime { get; set; }
    public DateTime? LastEpisodeAt { get; set; }
    public DateTime? LastEpisodesSync { get; set; }

    private DateTime? _nextEpisodeAt;
    public DateTime? NextEpisodeAt
    {
        get => _nextEpisodeAt;
        set
        {
            if (SetProperty(ref _nextEpisodeAt, value))
                NotifyNextEpisodeChanged();
        }
    }
}
