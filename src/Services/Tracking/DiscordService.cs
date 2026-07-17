using System;
using System.Collections.Generic;
using DiscordRPC;
using DiscordRPC.Logging;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Tracking;

public class DiscordService : IDisposable
{
    private DiscordRpcClient? _client;
    private readonly SettingsService _settingsService;
    private readonly object _gate = new();
    private const string DefaultClientId = "1496599223192391941"; // User's Client ID

    public DiscordService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Initialize()
    {
        lock (_gate)
        {
            if (!_settingsService.Current.System.EnableDiscordRPC) return;

            if (_client?.IsInitialized == true) return;

            try
            {
                _client?.Dispose();
                _client = new DiscordRpcClient(DefaultClientId);
                _client.Logger = new ConsoleLogger { Level = DiscordRPC.Logging.LogLevel.Warning };
                
                _client.OnReady += (sender, e) => Log.Information("Discord RPC Ready for {Username}", e.User.Username);
                _client.OnPresenceUpdate += (sender, e) => Log.Debug("Discord Presence Updated");

                _client.Initialize();
                Log.Information("Discord RPC Service Initialized");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize Discord RPC");
            }
        }
    }

    public void UpdatePresence(string displayTitle, string? episode, int totalEpisodes = 0, string? malUrl = null, string? shikiUrl = null, TimeSpan? position = null, TimeSpan? duration = null, string? imageUrl = null, bool isPlaying = true)
    {
        lock (_gate)
        {
            if (_client == null || !_settingsService.Current.System.EnableDiscordRPC) return;

            try
            {
                string state;
                if (totalEpisodes > 0)
                {
                    state = string.Format(UIUtils.GetLoc("anime.labels.series_of"), episode, totalEpisodes);
                }
                else
                {
                    state = string.Format(UIUtils.GetLoc("anime.labels.series_only"), episode);
                }

                Timestamps? timestamps = null;
                if (isPlaying && position.HasValue && duration.HasValue && position.Value.TotalSeconds > 0 && duration.Value.TotalSeconds > 0)
                {
                    var now = DateTime.UtcNow;
                    timestamps = new Timestamps()
                    {
                        Start = now.Subtract(position.Value),
                        End = now.Subtract(position.Value).Add(duration.Value)
                    };
                }
                else if (isPlaying)
                {
                    timestamps = Timestamps.Now;
                }
                else if (!isPlaying && position.HasValue && duration.HasValue && duration.Value.TotalSeconds > 0)
                {
                    string format = duration.Value.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss";
                    state += $" [{position.Value.ToString(format)} / {duration.Value.ToString(format)}]";
                }

                var presence = new RichPresence()
                {
                    Type = ActivityType.Watching,
                    StatusDisplay = StatusDisplayType.Details,
                    Details = TruncateDiscordText(displayTitle),
                    State = TruncateDiscordText(state),
                    Assets = new Assets()
                    {
                        LargeImageKey = !string.IsNullOrEmpty(imageUrl) && imageUrl.Length <= 256 ? imageUrl : "icon_large",
                        LargeImageText = TruncateDiscordText(displayTitle),
                        SmallImageKey = isPlaying ? "play_icon" : "pause_icon",
                        SmallImageText = isPlaying ? "Watching" : "Paused"
                    },
                    Timestamps = timestamps
                };

                // Discord Rich Presence allows at most 2 buttons per presence — that's a hard
                // SDK limit, not ours, so a third row (e.g. Anilist/AniDB) isn't possible here.
                var buttons = new List<Button>();
                if (!string.IsNullOrEmpty(malUrl)) buttons.Add(new Button { Label = "MyAnimeList", Url = malUrl });
                if (!string.IsNullOrEmpty(shikiUrl)) buttons.Add(new Button { Label = "Shikimori", Url = shikiUrl });

                if (buttons.Count > 0) presence.Buttons = buttons.ToArray();

                _client.SetPresence(presence);
                Log.Debug("Updating Discord Presence: {Details} | {State}", displayTitle, presence.State);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating Discord presence");
            }
        }
    }

    private static string? TruncateDiscordText(string? value)
    {
        return value?.Length > 128 ? value.Substring(0, 125) + "..." : value;
    }

    public void ClearPresence()
    {
        lock (_gate)
        {
            _client?.ClearPresence();
        }
    }

    public void UpdateStatus(bool enabled)
    {
        lock (_gate)
        {
            if (enabled)
            {
                if (_client == null) Initialize();
                else if (!_client.IsInitialized) _client.Initialize();
            }
            else
            {
                _client?.ClearPresence();
                _client?.Deinitialize();
                // DiscordRpcClient owns a named pipe + worker thread; Deinitialize alone
                // does not release them. Dispose to avoid leaking on every toggle.
                _client?.Dispose();
                _client = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
        }
    }
}
