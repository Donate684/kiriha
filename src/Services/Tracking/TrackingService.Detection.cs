using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Core.Player;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Models.Messages;
using Serilog;

namespace Kiriha.Services.Tracking;

public partial class TrackingService
{
    private void OnScrobbleCountdownUpdated(object? sender, string e)
    {
        _uiDispatcher.Post(() => WeakReferenceMessenger.Default.Send(new TrackingCountdownMessage(e)));
    }

    private void OnMediaCleared(object? sender, EventArgs e)
    {
        lock (_state)
        {
            _currentMedia = null;
            _matchedAnime = null;
        }
        _scrobbleService.CancelScrobble();

        _uiDispatcher.Post(() =>
        {
            WeakReferenceMessenger.Default.Send(new MediaChangedMessage(null));
            WeakReferenceMessenger.Default.Send(new AnimeMatchedMessage(null));
        });
        _discordService.ClearPresence();
    }

    private async void OnMediaDetected(object? sender, ParsedMedia media)
    {
        // async void event handler: any leaked exception bubbles to the
        // synchronization context (here: process-wide) and tears the app down.
        // The handler is invoked from AnisthesiaService's polling thread, so
        // we can't rely on a UI-thread safety net either.
        try
        {
            if (!_settingsService.Current.System.Scrobbler.Enabled) return;

            lock (_state)
            {
                if (_manualMapInProgress) return;
            }

            await MatchMediaAsync(media);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex) { Log.Error(ex, "TrackingService: OnMediaDetected failed for {Title}", media?.AnimeTitle); }
    }

    public void SetInternalMedia(InternalPlayerState state)
    {
        if (state.IsClosed)
        {
            ParsedMedia? current;
            lock (_state) current = _currentMedia;
            if (current != null && current.ProcessName == "KirihaInternal")
            {
                OnMediaCleared(this, EventArgs.Empty);
            }
            return;
        }

        var media = new ParsedMedia
        {
            ProcessName = "KirihaInternal",
            OriginalTitle = !string.IsNullOrEmpty(state.OriginalTitle) ? state.OriginalTitle : state.AnimeTitle,
            AnimeTitle = state.AnimeTitle,
            Episode = state.Episode,
            Position = TimeSpan.FromSeconds(state.Position),
            Duration = TimeSpan.FromSeconds(state.Duration),
            IsPlaying = state.IsPlaying,
            Pid = 0
        };

        if (state.AnimeId.HasValue)
        {
            _ = SetMatchedInternalMedia(media, state.AnimeId.Value);
        }
        else
        {
            _ = MatchMediaAsync(media);
        }
    }
}
