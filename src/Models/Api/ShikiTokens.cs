using System;
using System.Text.Json.Serialization;

namespace Kiriha.Models.Api;

public class ShikiTokens
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExpired => DateTime.UtcNow >= CreatedAt.AddSeconds(ExpiresIn - 60);

    // Shikimori specific: we might want to store user_id
    public int? UserId { get; set; }

    // Which mirror this token was issued by. Tokens are NOT cross-mirror compatible
    // because shikimori.one and shikimori.net are independent OAuth realms.
    public ShikiMirror Mirror { get; set; } = ShikiMirror.One;
}
