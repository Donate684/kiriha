using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kiriha.Models.Entities;

/// <summary>
/// Persistent HTTP response cache for conditional GETs (ETag / Last-Modified).
///
/// Used by <see cref="Kiriha.Services.Api.MalApiService"/> on seasonal and
/// search endpoints. Hits replay the body locally; misses go through the
/// network with conditional headers attached so MAL can respond 304 and skip
/// the body transfer entirely.
///
/// Keyed by SHA-256(url) instead of url itself because MAL request URLs are
/// long (full <c>fields=</c> list) and SQLite primary-key comparisons on a
/// fixed-size hex string are noticeably faster than on KB-sized strings.
/// </summary>
[Table("http_response_cache")]
public class HttpCacheEntry
{
    [Key]
    [Column("url_hash")]
    public string UrlHash { get; set; } = string.Empty;

    [Column("etag")]
    public string? ETag { get; set; }

    [Column("last_modified")]
    public string? LastModified { get; set; }

    [Column("body")]
    public byte[] Body { get; set; } = Array.Empty<byte>();

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
