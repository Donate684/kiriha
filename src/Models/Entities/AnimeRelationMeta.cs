using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

[Table("anime_relation_meta")]
public class AnimeRelationMeta
{
    [Key]
    [Column("mal_id")]
    public int MalId { get; set; }

    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }
}
