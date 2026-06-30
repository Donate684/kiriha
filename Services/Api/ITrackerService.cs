using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.Services.Api;

public interface ITrackerService
{
    string Name { get; }
    bool IsEnabled { get; }

    Task<List<AnimeItem>?> GetUserAnimeListAsync(CancellationToken ct = default);
    Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default);
    Task<SyncOutcome> SaveFullListStatusAsync(AnimeItem item, CancellationToken ct = default);

    Task<List<AnimeItem>> SearchAnimeAsync(string query, CancellationToken ct = default);
    Task<AnimeItem?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default);
    Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default);

    Task<List<AnimeItem>?> GetUserMangaListAsync(CancellationToken ct = default);
    Task<SyncOutcome> UpdateMangaProgressAsync(int mangaId, int chapters, int? volumes = null, UserAnimeStatus? status = null, int? score = null, CancellationToken ct = default);
    Task<List<AnimeItem>> SearchMangaAsync(string query, CancellationToken ct = default);
    Task<AnimeItem?> GetMangaDetailsAsync(int mangaId, CancellationToken ct = default);
}
