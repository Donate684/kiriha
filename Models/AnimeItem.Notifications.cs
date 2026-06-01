namespace Kiriha.Models;

public partial class AnimeItem
{
    private void NotifyTitleChanged()
    {
        OnPropertyChanged(nameof(Presentation));
    }

    private void NotifySynopsisChanged()
    {
        OnPropertyChanged(nameof(Presentation));
    }

    private void NotifySeasonChanged()
    {
        OnPropertyChanged(nameof(Season));
    }

    private void NotifyEpisodesAiredChanged()
    {
        OnPropertyChanged(nameof(Presentation));
    }

    private void NotifyNextEpisodeChanged()
    {
        OnPropertyChanged(nameof(Presentation));
    }

    private void NotifyProgressChanges()
    {
        OnPropertyChanged(nameof(Presentation));
    }

    public void RefreshMetadata() => OnPropertyChanged(string.Empty);

    /// <summary>
    /// Refreshes only the time-dependent airing badge properties.
    /// </summary>
    public void RefreshAiringBadge()
    {
        OnPropertyChanged(nameof(Presentation));
    }
}
