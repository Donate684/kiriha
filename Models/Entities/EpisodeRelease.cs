using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

[Table("episode_releases")]
public class EpisodeRelease
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("id")]
    public int Id { get; set; }

    [Column("mal_id")]
    public int MalId { get; set; }

    [Column("episode_number")]
    public int EpisodeNumber { get; set; }

    [Column("air_date")]
    public DateTime? AirDate { get; set; }

    [Column("title")]
    public string? Title { get; set; }
}
