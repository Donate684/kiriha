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

    public bool IsAnime => _item.MediaKind == MediaKind.Anime;
    public bool IsManga => _item.MediaKind != MediaKind.Anime;

    public double ProgressValue
    {
        get
        {
            var total = EffectiveTotal;
            if (total <= 0) return 0;
            var progress = IsManga ? _item.ChaptersRead : _item.Progress;
            return Math.Clamp((double)progress / total * 100, 0, 100);
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
            if (IsManga) return false; // Manga doesn't have an aired schedule in Kiriha yet
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
            if (IsManga && _item.VolumesRead > 0)
                return $"{ProgressPart} {TotalPart} | {_item.VolumesRead} {UIUtils.GetLoc("anime.labels.total_vol_format", _item.Volumes > 0 ? _item.Volumes.ToString() : "?")}";
            return $"{ProgressPart} {TotalPart}";
        }
    }

    public bool CanEditProgress => !IsCompleted || _item.IsRewatching;

    public bool IsCompleted => _item.Status == UserAnimeStatus.Completed;

    public bool ShowProgress => !IsCompleted || _item.IsRewatching;

    public bool HasNewEpisodes => _item.Status == UserAnimeStatus.Watching && _item.Progress < ResolvedAiredEpisodes;

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
            if (_item.Status == UserAnimeStatus.Dropped) return string.Empty;

            if (IsNewEpisode && HasNewEpisodes) return UIUtils.GetLoc("anime.labels.new_ep");
            if (_item.NextEpisodeAt.HasValue)
            {
                if (_item.StatusDetailed?.Equals("finished_airing", StringComparison.OrdinalIgnoreCase) == true || _item.StatusDetailed?.Equals("finished airing", StringComparison.OrdinalIgnoreCase) == true)
                    return string.Empty;

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

    public string ProgressPart => IsManga ? _item.ChaptersRead.ToString() : _item.Progress.ToString();

    public string TotalPart
    {
        get
        {
            var isManga = _item.MediaKind != MediaKind.Anime;
            var totalCount = isManga ? _item.Chapters : _item.TotalEpisodes;
            var total = totalCount > 0 ? totalCount.ToString() : "?";
            if (isManga)
            {
                if (IsCompleted && !_item.IsRewatching)
                    return UIUtils.GetLoc("anime.labels.total_ch_finished", total);
                return UIUtils.GetLoc("anime.labels.total_ch_format", total);
            }
            if (IsCompleted && !_item.IsRewatching)
                return UIUtils.GetLoc("anime.labels.total_ep_finished", total);
            return UIUtils.GetLoc("anime.labels.total_ep_format", total);
        }
    }

    public string EpisodesDisplay
    {
        get
        {
            var episodes = _item.TotalEpisodes > 0 ? _item.TotalEpisodes.ToString() : "?";
            var format = IsCompleted && !_item.IsRewatching ? "anime.labels.total_ep_finished" : "anime.labels.total_ep_format";
            var totalPart = UIUtils.GetLoc(format, episodes);
            if (IsCompleted && !_item.IsRewatching) return totalPart;
            return $"{_item.Progress} {totalPart}";
        }
    }

    public string ChaptersDisplay
    {
        get
        {
            var chapters = _item.Chapters > 0 ? _item.Chapters.ToString() : "?";
            var format = IsCompleted && !_item.IsRewatching ? "anime.labels.total_ch_finished" : "anime.labels.total_ch_format";
            var totalPart = UIUtils.GetLoc(format, chapters);
            if (IsCompleted && !_item.IsRewatching) return totalPart;
            return $"{_item.ChaptersRead} {totalPart}";
        }
    }

    public string VolumesDisplay
    {
        get
        {
            var volumes = _item.Volumes > 0 ? _item.Volumes.ToString() : "?";
            var format = IsCompleted && !_item.IsRewatching ? "anime.labels.total_vol_finished" : "anime.labels.total_vol_format";
            var totalPart = UIUtils.GetLoc(format, volumes);
            if (IsCompleted && !_item.IsRewatching) return totalPart;
            return $"{_item.VolumesRead} {totalPart}";
        }
    }

    public string DisplayAiringStatus
    {
        get
        {
            return _item.StatusDetailed?.ToLowerInvariant() switch
            {
                "currently_airing" or "currently airing" => UIUtils.GetLoc("anime.status.currently_airing"),
                "finished_airing" or "finished airing" => UIUtils.GetLoc("anime.status.finished_airing"),
                "not_yet_aired" or "not yet aired" or "anons" => UIUtils.GetLoc("anime.status.not_yet_aired"),
                _ => _item.StatusDetailed != null ? UIUtils.GetLoc("anime.status." + _item.StatusDetailed.ToLowerInvariant().Replace(" ", "_")) : UIUtils.GetLoc("anime.status.unknown")
            };
        }
    }

    public bool ShowAiredInfo => IsAnime && !(_item.StatusDetailed?.Equals("finished_airing", StringComparison.OrdinalIgnoreCase) == true || _item.StatusDetailed?.Equals("finished airing", StringComparison.OrdinalIgnoreCase) == true);

    public bool HasGenres => _item.Genres != null && _item.Genres.Count > 0;

    public IEnumerable<string> TopGenres => _item.Genres?.Take(2) ?? Enumerable.Empty<string>();

    public bool HasStudios => _item.Studios != null && _item.Studios.Count > 0;

    public int EffectiveTotal
    {
        get
        {
            if (_item.MediaKind != MediaKind.Anime)
            {
                return _item.Chapters > 0 ? _item.Chapters : Math.Max(_item.ChaptersRead, 1);
            }

            if (_item.TotalEpisodes > 0) return _item.TotalEpisodes;
            int maxKnownEpisode = Math.Max(_item.Progress, _item.EpisodesAired);
            if (maxKnownEpisode <= 0) return 12;
            return maxKnownEpisode > 24 ? ((maxKnownEpisode - 1) / 12 + 1) * 12 : (maxKnownEpisode > 12 ? 24 : 12);
        }
    }

    public int ResolvedAiredEpisodes => _item.StatusDetailed == "finished_airing" && _item.TotalEpisodes > 0
        ? _item.TotalEpisodes
        : _item.EpisodesAired;
}
