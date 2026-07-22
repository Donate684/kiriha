using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Api;

public partial class MalApiService
{
    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_watched_episodes", episodes.ToString()) };
        if (score.HasValue) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None) values.Add(new("status", StatusMapper.ToMal(status.Value)));
        if (isRewatching.HasValue) values.Add(new("is_rewatching", isRewatching.Value.ToString().ToLower()));
        if (rewatchCount.HasValue) values.Add(new("num_times_rewatched", rewatchCount.Value.ToString()));

        return await SendPatchAsync($"anime/{animeId}/my_list_status", values, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeItem item, CancellationToken ct = default)
    {
        bool isManga = item.MediaKind != MediaKind.Anime;

        var values = new List<KeyValuePair<string, string>>
        {
            new(isManga ? "num_chapters_read" : "num_watched_episodes", isManga ? item.ChaptersRead.ToString() : item.Progress.ToString()),
            new("status", StatusMapper.ToMal(item.Status, isManga)),
            new("num_times_rewatched", item.RewatchCount.ToString()),
            new("is_rewatching", item.IsRewatching.ToString().ToLower())
        };

        if (isManga)
        {
            values.Add(new("num_volumes_read", item.VolumesRead.ToString()));
        }

        if (int.TryParse(item.Score, out int s)) values.Add(new("score", s.ToString()));
        if (!string.IsNullOrEmpty(item.Notes)) values.Add(new("notes", item.Notes));
        if (item.DateStarted.HasValue) values.Add(new("start_date", item.DateStarted.Value.ToString("yyyy-MM-dd")));
        if (item.DateCompleted.HasValue) values.Add(new("finish_date", item.DateCompleted.Value.ToString("yyyy-MM-dd")));

        var endpoint = isManga ? $"manga/{item.Id}/my_list_status" : $"anime/{item.Id}/my_list_status";
        return await SendPatchAsync(endpoint, values, ct);
    }

    public async Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default)
    {
        // MAL returns 404 if the anime is already not on the list — treat as Success so
        // a redundant Remove doesn't get queued forever after the user deleted it via web.
        var outcome = await SendRequestAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, MalBaseUrl + $"anime/{animeId}/my_list_status"),
            ct);
        return outcome == SyncOutcome.PermanentFailure ? SyncOutcome.Success : outcome;
    }

    public async Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default)
    {
        var values = new List<KeyValuePair<string, string>> { new("num_chapters_read", chapters.ToString()) };
        if (volumes.HasValue) values.Add(new("num_volumes_read", volumes.Value.ToString()));
        if (score.HasValue) values.Add(new("score", score.Value.ToString()));
        if (status != null && status != UserAnimeStatus.None) values.Add(new("status", StatusMapper.ToMal(status.Value, true)));

        return await SendPatchAsync($"manga/{mangaId}/my_list_status", values, ct);
    }
}
