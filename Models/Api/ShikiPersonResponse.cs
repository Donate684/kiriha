using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kiriha.Models.Api;

public class ShikiPersonResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("works")]
    public List<ShikiPersonWork>? Works { get; set; }
}

public class ShikiPersonWork
{
    [JsonPropertyName("anime")]
    public ShikiPersonWorkAnime? Anime { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

public class ShikiPersonWorkAnime
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("russian")]
    public string? Russian { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("score")]
    public string? Score { get; set; }
}
