using System;

namespace Kiriha.Models.Entities;

public class AnimeStaff
{
    public int Id { get; set; }
    public int SourceMalId { get; set; }
    public int PersonMalId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string PersonUrl { get; set; } = string.Empty;
    public string PersonImageUrl { get; set; } = string.Empty;
    public string Positions { get; set; } = string.Empty;
}
