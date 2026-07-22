using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

[Table("offline_anime")]
public class AnimeOfflineItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("id")]
    public int Id { get; set; } // We use MyAnimeList ID as primary key if available, or a hash

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    private string _type = string.Empty;

    [Column("type")]
    public string Type
    {
        get
        {
            if (string.IsNullOrEmpty(_type)) return "";
            var upper = _type.ToUpperInvariant();
            return (upper == "TV" || upper == "OVA" || upper == "ONA")
                ? upper
                : char.ToUpperInvariant(_type[0]) + _type.Substring(1).ToLowerInvariant();
        }
        set => _type = value;
    }

    [Column("status")]
    public string Status { get; set; } = string.Empty;

    [Column("episodes")]
    public int TotalEpisodes { get; set; }

    [Column("season")]
    public string Season { get; set; } = string.Empty;

    [Column("year")]
    public int? Year { get; set; }

    [Column("synonyms_json")]
    public string SynonymsJson { get; set; } = "[]"; // JSON array of alternative titles

    [Column("relations_json")]
    public string RelationsJson { get; set; } = "[]"; // JSON array of related anime URLs/IDs

    [Column("mal_id")]
    public int? MalId { get; set; }

    [Column("anilist_id")]
    public int? AniListId { get; set; }

    [Column("kitsu_id")]
    public int? KitsuId { get; set; }

    [Column("last_updated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public string? ImageUrl { get; set; }
}
