using System;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Core;
using Kiriha.Models.Entities;

namespace Kiriha.Models;

public readonly struct AnimeItemPresentation
{
    private readonly AnimeItem _item;
    private readonly DateTime _now;

    public AnimeItemPresentation(AnimeItem item) : this(item, DateTime.Now)
    {
    }

    public AnimeItemPresentation(AnimeItem item, DateTime now)
    {
        _item = item;
        _now = now;
    }

    public string DisplayTitle => !string.IsNullOrEmpty(_item.RussianTitle) ? _item.RussianTitle : _item.Title;

    public string? DisplaySynopsis => !string.IsNullOrEmpty(_item.RussianSynopsis) ? _item.RussianSynopsis : _item.Synopsis;

    public double ProgressValue
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            return Math.Clamp((double)_item.Progress / total * 100, 0, 100);
        }
    }

    public double ProgressValueFraction => ProgressValue / 100.0;

    public double AiredValueFraction
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            var aired = ResolvedAiredEpisodes;
            if (aired <= 0) return 0;
            return Math.Clamp((double)aired / total, 0, 1);
        }
    }

    public bool ShowAiredProgressBar
    {
        get
        {
            if (_item.Status != UserAnimeStatus.Watching || IsCompleted) return false;
            var aired = ResolvedAiredEpisodes;
            if (aired <= 0) return false;
            return _item.Progress < aired;
        }
    }

    public string ProgressDisplay
    {
        get
        {
            if (IsCompleted && !_item.IsRewatching) return TotalPart;
            return $"{ProgressPart} {TotalPart}";
        }
    }

    public bool CanEditProgress => !IsCompleted || _item.IsRewatching;

    public bool IsCompleted => _item.Status == UserAnimeStatus.Completed;

    public bool ShowProgress => !IsCompleted || _item.IsRewatching;

    public bool HasNewEpisodes => _item.StatusDetailed == "currently_airing"
        && _item.Status == UserAnimeStatus.Watching
        && _item.Progress < _item.EpisodesAired;

    public int UnseenEpisodesCount
    {
        get
        {
            if (!ShowAiredProgressBar) return 0;
            return Math.Max(0, ResolvedAiredEpisodes - _item.Progress);
        }
    }

    public bool IsNewEpisode => _item.LastEpisodeAt.HasValue && (_now - _item.LastEpisodeAt.Value).TotalDays < 2;

    public string AiringBadgeText
    {
        get
        {
            if (IsNewEpisode && HasNewEpisodes) return UIUtils.GetLoc("anime.labels.new_ep");
            if (_item.NextEpisodeAt.HasValue)
            {
                var diff = _item.NextEpisodeAt.Value - _now;

                if (diff.TotalSeconds <= 0)
                {
                    if (diff.TotalHours < -48) return string.Empty;
                    return UIUtils.GetLoc("anime.labels.new_ep") + "?";
                }

                if (diff.TotalDays >= 1)
                    return $"{(int)diff.TotalDays}{UIUtils.GetLoc("common.time.day_abbr")}";

                if (diff.TotalHours >= 1)
                    return $"{diff.Hours}{UIUtils.GetLoc("common.time.hour_abbr")} {diff.Minutes}{UIUtils.GetLoc("common.time.min_abbr")}";

                return $"{diff.Minutes}{UIUtils.GetLoc("common.time.min_abbr")}";
            }

            return string.Empty;
        }
    }

    public string NextEpisodeAtDisplay => _item.NextEpisodeAt?.ToString("g") ?? "-";

    public string AiringBadgeColor
    {
        get
        {
            if (IsNewEpisode && HasNewEpisodes) return "#FF4500";
            if (_item.NextEpisodeAt.HasValue && (_item.NextEpisodeAt.Value - _now).TotalSeconds <= 0) return "#FF8C00";
            return "#4CAF50";
        }
    }

    public string ProgressPart => _item.Progress.ToString();

    public string TotalPart
    {
        get
        {
            var total = _item.TotalEpisodes > 0 ? _item.TotalEpisodes.ToString() : "?";
            if (IsCompleted && !_item.IsRewatching)
                return UIUtils.GetLoc("anime.labels.total_ep_finished", total);
            return UIUtils.GetLoc("anime.labels.total_ep_format", total);
        }
    }

    public string DisplayAiringStatus
    {
        get
        {
            return _item.StatusDetailed switch
            {
                "currently_airing" => UIUtils.GetLoc("anime.status.currently_airing"),
                "finished_airing" => UIUtils.GetLoc("anime.status.finished_airing"),
                "not_yet_aired" => UIUtils.GetLoc("anime.status.not_yet_aired"),
                _ => _item.StatusDetailed != null ? UIUtils.GetLoc("anime.status." + _item.StatusDetailed) : UIUtils.GetLoc("anime.status.unknown")
            };
        }
    }

    public bool ShowAiredInfo => _item.StatusDetailed != "finished_airing";

    public bool HasGenres => _item.Genres != null && _item.Genres.Count > 0;

    public IEnumerable<string> TopGenres => _item.Genres?.Take(2) ?? Enumerable.Empty<string>();

    public bool HasStudios => _item.Studios != null && _item.Studios.Count > 0;

    private int EffectiveTotal
    {
        get
        {
            if (_item.TotalEpisodes > 0) return _item.TotalEpisodes;
            int maxKnownEpisode = Math.Max(_item.Progress, _item.EpisodesAired);
            if (maxKnownEpisode <= 0) return 0;
            return maxKnownEpisode > 24 ? ((maxKnownEpisode - 1) / 12 + 1) * 12 : (maxKnownEpisode > 12 ? 24 : 12);
        }
    }

    private int ResolvedAiredEpisodes => _item.StatusDetailed == "finished_airing" && _item.TotalEpisodes > 0
        ? _item.TotalEpisodes
        : _item.EpisodesAired;
}
