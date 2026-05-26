using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.Models;

public partial class TorrentItem : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string? AnimeTitle { get; set; }
    public string? Episode { get; set; }
    public string? Resolution { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? MagnetLink { get; set; }
    public string? DownloadLink { get; set; }
    public DateTime PublishDate { get; set; }
    public bool IsNew { get; set; }
    public bool IsMatched { get; set; } // Matches user list
}
