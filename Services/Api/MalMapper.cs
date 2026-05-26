using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Api;

public static class MalMapper
{
    public static AnimeItem MapJsonToAnimeItem(JsonElement node)
    {
        var item = new AnimeItem
        {
            Id = node.GetProperty("id").GetInt32(),
            Title = node.GetProperty("title").GetString() ?? "",
            MainPictureUrl = GetMainPicture(node),
            Synopsis = node.GetOptionalString("synopsis"),
            TotalEpisodes = node.GetOptionalInt("num_episodes") ?? 0,
            MeanScore = node.TryGetProperty("mean", out var mean) ? mean.GetDouble().ToString("0.00") : null,
            Popularity = node.GetOptionalInt("popularity") ?? 0,
            Rank = node.GetOptionalInt("rank") ?? 0,
            AiringDate = node.GetOptionalDateTime("start_date"),
            Nsfw = node.GetOptionalString("nsfw") == "white" ? "No" : "Yes",
            StatusDetailed = node.GetOptionalString("status"),
            Rating = node.GetOptionalString("rating"),
            Type = node.GetOptionalString("media_type")?.ToUpper() ?? "TV"
        };

        if (node.TryGetProperty("start_season", out var ss))
        {
            item.StartYear = ss.GetOptionalInt("year");
            item.StartSeason = ss.GetOptionalString("season");
        }

        if (node.TryGetProperty("broadcast", out var b))
        {
            item.BroadcastDay = b.GetOptionalString("day_of_the_week");
            item.BroadcastTime = b.GetOptionalString("start_time");
        }

        MapAlternativeTitles(node, item);
        MapGenresAndStudios(node, item);

        if (node.TryGetProperty("my_list_status", out var ls) || node.TryGetProperty("list_status", out ls))
            MapListStatus(ls, item);

        return item;
    }

    public static string? GetMainPicture(JsonElement node)
    {
        if (!node.TryGetProperty("main_picture", out var mp)) return null;
        return mp.GetOptionalString("large") ?? mp.GetOptionalString("medium");
    }

    public static void MapAlternativeTitles(JsonElement node, AnimeItem item)
    {
        if (!node.TryGetProperty("alternative_titles", out var alts)) return;

        item.EnglishTitle = alts.GetOptionalString("en");
        item.JapaneseTitle = alts.GetOptionalString("ja");

        if (!string.IsNullOrEmpty(item.EnglishTitle)) item.AlternativeTitles.Add(item.EnglishTitle);
        if (!string.IsNullOrEmpty(item.JapaneseTitle)) item.AlternativeTitles.Add(item.JapaneseTitle);

        if (alts.TryGetProperty("synonyms", out var syns))
        {
            foreach (var s in syns.EnumerateArray())
            {
                var val = s.GetString();
                if (!string.IsNullOrEmpty(val)) item.AlternativeTitles.Add(val);
            }
        }
    }

    public static void MapGenresAndStudios(JsonElement node, AnimeItem item)
    {
        if (node.TryGetProperty("genres", out var gs))
        {
            item.Genres.Clear();
            item.Genres.AddRange(gs.EnumerateArray().Select(g => g.GetProperty("name").GetString() ?? ""));
        }

        if (node.TryGetProperty("studios", out var sts))
        {
            item.Studios.Clear();
            item.Studios.AddRange(sts.EnumerateArray().Select(s => s.GetProperty("name").GetString() ?? ""));
        }
    }

    public static void MapListStatus(JsonElement listStatus, AnimeItem item)
    {
        item.Status = StatusMapper.FromMal(listStatus.GetOptionalString("status"));
        item.Progress = listStatus.GetOptionalInt("num_watched_episodes") ?? listStatus.GetOptionalInt("num_episodes_watched") ?? 0;

        if (listStatus.TryGetProperty("score", out var scoreElement))
        {
            int score = scoreElement.GetInt32();
            item.Score = score == 0 ? "-" : score.ToString();
        }

        item.Notes = listStatus.GetOptionalString("notes");
        item.RewatchCount = listStatus.GetOptionalInt("num_times_rewatched") ?? 0;
        item.IsRewatching = listStatus.TryGetProperty("is_rewatching", out var ir) && ir.GetBoolean();
        item.DateStarted = listStatus.GetOptionalDateTime("start_date");
        item.DateCompleted = listStatus.GetOptionalDateTime("finish_date");
    }

    private static string? GetOptionalString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null;

    private static int? GetOptionalInt(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var p) && p.TryGetInt32(out var val) ? val : null;

    private static DateTime? GetOptionalDateTime(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var p) && DateTime.TryParse(p.GetString(), out var date) ? date : null;
}
