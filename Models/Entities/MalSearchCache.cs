using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

/// <summary>
/// Persistent cache of normalized-search-query → MAL anime id resolutions.
///
/// Populated by <see cref="Kiriha.Services.Data.MappingService.SearchOnMalAsync"/>
/// whenever the in-memory session cache misses but MAL gives us an answer
/// (or definitively does NOT — anime_id = 0 marks a negative cache entry).
///
/// Survives restarts so that re-scanning the same library / re-detecting the
/// same window title doesn't re-hit MAL for queries we've already resolved.
/// </summary>
[Table("mal_search_cache")]
public class MalSearchCache
{
    [Key]
    [Column("query_normalized")]
    public string QueryNormalized { get; set; } = string.Empty;

    /// <summary>Resolved MAL id. 0 marks a confirmed-negative cache entry.</summary>
    [Column("anime_id")]
    public int AnimeId { get; set; }

    /// <summary>Best-match score recorded at write time (diagnostic only).</summary>
    [Column("score")]
    public float Score { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
