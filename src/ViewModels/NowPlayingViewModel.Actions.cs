using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core;
using Kiriha.Core.Platform;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Serilog;

namespace Kiriha.ViewModels;

public partial class NowPlayingViewModel
{
    [RelayCommand]
    private async Task AddToWatching()
    {
        if (MatchedAnime == null) return;

        try
        {
            if (await _progressService.UpdateProgressAsync(MatchedAnime, MatchedAnime.Progress, UserAnimeStatus.Watching))
            {
                await _animeRepo.AddOrUpdateAnimeAsync(MatchedAnime);
                WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
            }

            OnPropertyChanged(nameof(IsNotInList));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add anime to watching");
        }
    }

    [RelayCommand]
    private async Task SelectSuggestion(object parameter)
    {
        if (parameter is not AnimeItem suggestion) return;

        Log.Information("Selecting anime suggestion: {Title} (ID: {Id})", suggestion.Title, suggestion.Id);
        LogDetection(CurrentMedia ?? new Kiriha.Models.ParsedMedia { AnimeTitle = suggestion.Title }, UIUtils.GetLoc("scrobbler.status.mapped_by") + " " + suggestion.DisplayTitle);

        Volatile.Write(ref _pendingManualMatchId, suggestion.Id);
        ShowSuggestions = false;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));

        try
        {
            MatchedAnime = suggestion;
            IsManuallyMapped = true;
            await _trackingService.ManualMapAsync(suggestion.Id);
            // Ensure it stays set — background AnimeMatched will eventually arrive
            // with the same id and clear _pendingManualMatchId from OnAnimeMatched.
            MatchedAnime = suggestion;
            IsManuallyMapped = true;
        }
        catch
        {
            // On error, drop the pending guard so the UI isn't permanently stuck.
            Volatile.Write(ref _pendingManualMatchId, 0);
            throw;
        }
    }

    [RelayCommand]
    private void DismissSuggestions()
    {
        ShowSuggestions = false;
        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
    }

    [RelayCommand]
    private async Task SearchSuggestions()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        var cts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _searchCts, cts);
        try { oldCts?.Cancel(); } catch (Exception ex) { Log.Debug(ex, "Error canceling search CTS"); }
        oldCts?.Dispose();

        IsSearching = true;
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _disposeCts.Token);
            var results = await _malApi.SearchAnimeAsync(SearchQuery, linkedCts.Token);
            if (linkedCts.Token.IsCancellationRequested) return;

            Suggestions.Clear();

            foreach (var r in results)
            {
                Suggestions.Add(r);
            }

            ShowSuggestions = Suggestions.Count > 0;
            OnPropertyChanged(nameof(HasSuggestions));
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to search anime inline");
        }
        finally
        {
            if (_searchCts == cts)
                IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task ManualMatch()
    {
        if (CurrentMedia == null) return;

        SearchQuery = CurrentMedia.AnimeTitle;
        await SearchSuggestions();
    }

    [RelayCommand]
    private async Task OpenSearchPanel()
    {
        IsSearchPanelOpen = true;
        if (string.IsNullOrWhiteSpace(SearchQuery) && CurrentMedia != null)
            SearchQuery = CurrentMedia.AnimeTitle;
        if (Suggestions.Count == 0 && !string.IsNullOrWhiteSpace(SearchQuery))
            await SearchSuggestions();
    }

    [RelayCommand]
    private void CloseSearchPanel()
    {
        IsSearchPanelOpen = false;
    }

    [RelayCommand]
    private async Task RemoveMapping()
    {
        await _trackingService.RemoveManualMappingAsync();
    }

    [RelayCommand]
    private async Task UnlinkMatch()
    {
        if (IsManuallyMapped)
        {
            // Remove persisted manual mapping; tracking service will re-match.
            await _trackingService.RemoveManualMappingAsync();
        }
        else
        {
            // Auto-match: persist a negative mapping so future sessions won't auto-match either.
            await _trackingService.AddNegativeMappingAsync();
            MatchedAnime = null;
            IsManuallyMapped = false;
            if (CurrentMedia != null) SearchQuery = CurrentMedia.AnimeTitle;
            await OpenSearchPanel();
        }
    }

    [RelayCommand]
    private void GoToSettings()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage(NavigationPage.Settings));
    }

    [RelayCommand]
    private void ConfirmMatch()
    {
        if (PendingMatch == null) return;
        MatchedAnime = PendingMatch;
        PendingMatch = null;
    }

    [RelayCommand]
    private void RejectMatch()
    {
        PendingMatch = null;
    }

    [RelayCommand]
    private async Task CopyMalLink()
    {
        if (MatchedAnime == null) return;
        string url = $"{Kiriha.Core.Constants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private async Task CopyShikiLink()
    {
        if (MatchedAnime == null) return;
        string url = $"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}";
        await CopyToClipboard(url);
    }

    [RelayCommand]
    private void OpenMalLink()
    {
        if (MatchedAnime == null) return;
        ShellLauncher.OpenUrl($"{Kiriha.Core.Constants.Api.Mal.WebsiteUrl}{MatchedAnime.Id}");
    }

    [RelayCommand]
    private void OpenShikiLink()
    {
        if (MatchedAnime == null) return;
        ShellLauncher.OpenUrl($"{ShikiEndpoints.WebsiteUrl(_settingsService.Current.Api.ShikiMirror)}{MatchedAnime.Id}");
    }

    private static async Task CopyToClipboard(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(text);
        }
    }
}
