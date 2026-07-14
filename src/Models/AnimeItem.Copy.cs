using System.Collections.Generic;

namespace Kiriha.Models;

public partial class AnimeItem
{
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

        if (AiredSourcePriority >= target.AiredSourcePriority || EpisodesAired > target.EpisodesAired)
        {
            target.EpisodesAired = EpisodesAired;
            target.AiredSourcePriority = AiredSourcePriority;
            target.LastEpisodeAt = LastEpisodeAt;
        }

        if (StatusDetailed == "finished_airing" || StatusDetailed == "finished airing") target.NextEpisodeAt = null;
        else if (NextEpisodeAt.HasValue) target.NextEpisodeAt = NextEpisodeAt;

        target.Genres = new List<string>(Genres);
        target.Studios = new List<string>(Studios);
        target.AlternativeTitles = new List<string>(AlternativeTitles);

        target.RefreshMetadata();
    }
}
