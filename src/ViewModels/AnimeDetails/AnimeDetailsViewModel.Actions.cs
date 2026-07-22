using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeDetailsViewModel
{
    [RelayCommand]
    private async Task CopyMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        string url = $"https://myanimelist.net/{type}/{Anime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        string baseUrl = Kiriha.Core.Shiki.ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
        string url = $"{baseUrl}{Anime.Id}";
        await CopyToClipboard(url);
    }

    private void OpenInBrowser(string url)
    {
        ShellLauncher.OpenUrl(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        string type = Anime.MediaKind == MediaKind.Anime ? "anime" : "manga";
        OpenInBrowser($"https://myanimelist.net/{type}/{Anime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        string baseUrl = ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror, Anime.MediaKind);
        OpenInBrowser($"{baseUrl}{Anime.Id}");
    }

    [RelayCommand]
    private void IncrementProgress()
    {
        if (Anime.MediaKind != MediaKind.Anime)
        {
            if (Anime.ChaptersRead < Anime.Chapters || Anime.Chapters == 0)
                Anime.ChaptersRead++;
        }
        else
        {
            if (Anime.Progress < Anime.TotalEpisodes || Anime.TotalEpisodes == 0)
                Anime.Progress++;
        }
    }

    [RelayCommand]
    private void IncrementVolumes()
    {
        if (Anime.VolumesRead < Anime.Volumes || Anime.Volumes == 0)
            Anime.VolumesRead++;
    }

    [RelayCommand]
    private void SetStartDateToToday()
    {
        Anime.DateStarted = System.DateTime.Now;
    }

    [RelayCommand]
    private void SetEndDateToToday()
    {
        Anime.DateCompleted = System.DateTime.Now;
    }

    [RelayCommand]
    private void AddToList()
    {
        Anime.Status = UserAnimeStatus.Watching;
        // The property change notification for IsInList is handled by the ctor event handler
    }

    public bool HasChanges
    {
        get
        {
            if (_originalAnime == null || Anime == null) return false;

            string currentScore = Anime.Score ?? "";
            if (currentScore != "-" && currentScore.Contains(" "))
                currentScore = currentScore.Split(' ')[0];

            string origScore = _originalAnime.Score ?? "";
            if (origScore != "-" && origScore.Contains(" "))
                origScore = origScore.Split(' ')[0];

            return _originalAnime.Status != Anime.Status ||
                   _originalAnime.Progress != Anime.Progress ||
                   _originalAnime.ChaptersRead != Anime.ChaptersRead ||
                   _originalAnime.VolumesRead != Anime.VolumesRead ||
                   origScore != currentScore ||
                   _originalAnime.IsRewatching != Anime.IsRewatching ||
                   _originalAnime.RewatchCount != Anime.RewatchCount ||
                   _originalAnime.Notes != Anime.Notes ||
                   _originalAnime.DateStarted != Anime.DateStarted ||
                   _originalAnime.DateCompleted != Anime.DateCompleted;
        }
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task Save(object? window)
    {
        if (_isRemoving) return;

        bool markedAsDropped = _originalAnime.Status != UserAnimeStatus.Dropped && Anime.Status == UserAnimeStatus.Dropped;
        bool markedAsCompleted = _originalAnime.Status != UserAnimeStatus.Completed && Anime.Status == UserAnimeStatus.Completed;

        // If the score ends with text (like "10 (Masterpiece)"), parse only the number
        string rawScore = Anime.Score;
        if (rawScore != "-" && rawScore.Contains(" "))
        {
            Anime.Score = rawScore.Split(' ')[0];
        }

        bool scoreChanged = _originalAnime.Score != Anime.Score && Anime.Score != "-" && !string.IsNullOrEmpty(Anime.Score);

        bool hasChanges = _originalAnime.Status != Anime.Status ||
                          _originalAnime.Progress != Anime.Progress ||
                          _originalAnime.ChaptersRead != Anime.ChaptersRead ||
                          _originalAnime.VolumesRead != Anime.VolumesRead ||
                          _originalAnime.Score != Anime.Score ||
                          _originalAnime.IsRewatching != Anime.IsRewatching ||
                          _originalAnime.RewatchCount != Anime.RewatchCount ||
                          _originalAnime.Notes != Anime.Notes ||
                          _originalAnime.DateStarted != Anime.DateStarted ||
                          _originalAnime.DateCompleted != Anime.DateCompleted;

        // Apply changes from clone to original item
        Anime.CopyTo(_originalAnime);

        if (_originalAnime.Status != UserAnimeStatus.None)
        {
            // Update local collection (add if new, update if exists)
            await _animeRepo.AddOrUpdateAnimeAsync(_originalAnime);

            if (markedAsDropped)
            {
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "Dropped");
            }
            if (markedAsCompleted)
            {
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "Completed");
            }
            if (scoreChanged)
            {
                _historyService.AddEntry(_originalAnime.Id, _originalAnime.Title, _originalAnime.RussianTitle, _originalAnime.Progress, "ScoreSet", _originalAnime.Score);
            }

            if (hasChanges)
            {
                // BACKGROUND SYNC: Enqueue the full item update
                await _syncManager.EnqueueFullUpdateAsync(_originalAnime);
            }

            // Notify UI to refresh lists
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }

        // Optimistically close window
        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    [RelayCommand]
    public async Task RemoveFromList(object window)
    {
        if (!IsDeleteConfirmationVisible)
        {
            IsDeleteConfirmationVisible = true;
            return;
        }

        _isRemoving = true;
        await _animeProgressService.RemoveAnimeAsync(_originalAnime.Id);

        // Notify UI to refresh lists
        WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());

        if (window is Avalonia.Controls.Window w) w.Close(true);
    }

    private async Task CopyToClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }

    [RelayCommand]
    private void ShowFranchiseGraph()
    {
        var vm = new FranchiseGraphViewModel(Anime.Id, _shikiApiService, _dialogs);
        var window = new Kiriha.Views.FranchiseGraphWindow
        {
            DataContext = vm
        };

        // Show as a non-modal window or modal, depending on preference. Non-modal is better so user can keep it open.
        window.Show();
    }
}
