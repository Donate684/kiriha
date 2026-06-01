using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

/// <summary>
/// Sidecar for <see cref="EpisodeRelease"/>: tracks when the Jikan episode
/// list for a given MAL id was last successfully fetched.
///
/// Used by <see cref="Kiriha.Services.Api.JikanApiService.GetEpisodeListAsync"/>
/// to gate live API calls behind a freshness window:
///   * Currently-airing: 12 h sliding TTL — episode lists barely change
///     between weekly broadcasts, so re-fetching every poll is pure waste.
///   * Finished airing: effectively infinite — the episode list is immutable
///     once a series ends; new specials get separate MAL entries.
/// </summary>
[Table("episode_list_meta")]
public class EpisodeListMeta
{
    [Key]
    [Column("mal_id")]
    public int MalId { get; set; }

    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }
}
