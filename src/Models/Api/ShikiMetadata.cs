using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kiriha.Models.Api;

public class ShikiMetadata
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("russian")]
    public string? Russian { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("episodes")]
    public int? Episodes { get; set; }

    [JsonPropertyName("episodes_aired")]
    public int? EpisodesAired { get; set; }

    [JsonPropertyName("next_episode_at")]
    public DateTime? NextEpisodeAt { get; set; }

    /// <summary>
    /// UTC timestamp of the last successful upsert. EF-only column —
    /// not part of the Shikimori response, so <see cref="JsonIgnoreAttribute"/>
    /// keeps deserializers from clobbering it on round-trip. Used by
    /// <c>ShikiMetadataService</c> to honour TTL for currently-airing shows
    /// whose <c>EpisodesAired</c> / <c>NextEpisodeAt</c> need refreshing
    /// after each new episode.
    /// </summary>
    [JsonIgnore]
    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }
}
