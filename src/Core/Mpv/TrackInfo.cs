namespace Kiriha.Core.Mpv;

public class TrackInfo
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Lang { get; set; }
    public bool Selected { get; set; }

    public string DisplayName
    {
        get
        {
            string name = Title ?? Lang ?? "Unknown Track";
            if (Title != null && Lang != null) name = $"{Title} ({Lang})";
            return name;
        }
    }
}
