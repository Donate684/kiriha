using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kiriha.Models.Api;

public class ShikiFranchiseResponse
{
    [JsonPropertyName("links")]
    public List<ShikiFranchiseLink> Links { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<ShikiFranchiseNode> Nodes { get; set; } = new();

    [JsonPropertyName("current_id")]
    public int CurrentId { get; set; }
}

public class ShikiFranchiseLink
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("source_id")]
    public int SourceId { get; set; }

    [JsonPropertyName("target_id")]
    public int TargetId { get; set; }

    [JsonPropertyName("source")]
    public int Source { get; set; }

    [JsonPropertyName("target")]
    public int Target { get; set; }

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("relation")]
    public string Relation { get; set; } = string.Empty;
}

public class ShikiFranchiseNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("date")]
    public long Date { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string ImageUrl { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("weight")]
    public int Weight { get; set; }
}
