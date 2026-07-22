using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

[Table("file_recognition_cache")]
public class FileRecognitionCache
{
    [Key]
    [Column("file_hash")]
    public string FileHash { get; set; } = string.Empty;

    [Column("anime_id")]
    public int AnimeId { get; set; }

    [Column("last_used")]
    public string LastUsed { get; set; } = string.Empty;
}
