using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Kiriha.Models;

public partial class AnimeItem
{
    private bool _isHiddenInSeasons;

    /// <summary>
    /// Client-only flag mirrored from AppSettings.UI.HiddenSeasonalIds.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsHiddenInSeasons
    {
        get => _isHiddenInSeasons;
        set => SetProperty(ref _isHiddenInSeasons, value);
    }

    private bool _isHideConfirming;

    /// <summary>
    /// Transient Seasonal view state for the hide-button confirmation.
    /// </summary>
    [NotMapped]
    [JsonIgnore]
    public bool IsHideConfirming
    {
        get => _isHideConfirming;
        set => SetProperty(ref _isHideConfirming, value);
    }
}
