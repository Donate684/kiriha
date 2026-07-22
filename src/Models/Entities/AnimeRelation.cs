namespace Kiriha.Models.Entities;

public class AnimeRelation
{
    public int Id { get; set; }
    public int SourceMalId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public int TargetMalId { get; set; }
    public string TargetType { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
}
