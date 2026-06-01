using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Api;

public class ShikiApiService : ITrackerService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ShikiAuthService _authService;
    private readonly ShikiHostResolver _hostResolver;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public string Name => "Shikimori";

    // Token must belong to the currently active mirror; otherwise it's effectively
    // useless because shikimori.one and shikimori.net are independent OAuth realms.
    public bool IsEnabled
    {
        get
        {
            var t = _settingsService.Current.Api.Shiki;
            return t != null && t.Mirror == _settingsService.Current.Api.ShikiMirror;
        }
    }

    private string ShikiBaseUrl => ShikiEndpoints.BaseUrl(_settingsService.Current.Api.ShikiMirror);

    public ShikiApiService(HttpClient httpClient, SettingsService settingsService, ShikiAuthService authService, ShikiHostResolver hostResolver)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _authService = authService;
        _hostResolver = hostResolver;
    }

    public Task<List<AnimeItem>?> GetUserAnimeListAsync(CancellationToken ct = default)
    {
        Log.Warning("Shikimori full-list sync is disabled; skipping destructive local mirror update.");
        return Task.FromResult<List<AnimeItem>?>(null);
    }

    public async Task<SyncOutcome> UpdateProgressAsync(int animeId, int episodes, UserAnimeStatus? status = null, int? score = null, bool? isRewatching = null, int? rewatchCount = null, CancellationToken ct = default)
    {
        // No tokens at all — user disabled the tracker. Nothing to retry.
        if (_settingsService.Current.Api.Shiki == null) return SyncOutcome.PermanentFailure;

        if (_settingsService.Current.Api.Shiki.UserId == null)
        {
            // Triggers EnsureValidTokenAsync internally which may *replace* the Shiki tokens
            // object in settings; therefore we must re-read after the call instead of mutating
            // the local reference (which would otherwise point at the orphaned old object).
            var userId = await GetCurrentUserIdAsync(ct);
            if (userId == null) return SyncOutcome.TransientFailure;
            var current = _settingsService.Current.Api.Shiki;
            if (current == null) return SyncOutcome.PermanentFailure;
            _settingsService.Update(settings =>
            {
                if (settings.Api.Shiki != null) settings.Api.Shiki.UserId = userId;
            }, save: false);
            _settingsService.SaveImmediate();
        }

        // Re-read to pick up any token-refresh side effects.
        var tokens = _settingsService.Current.Api.Shiki;
        if (tokens == null) return SyncOutcome.PermanentFailure;

        var userRate = new Dictionary<string, object>
        {
            ["user_id"] = tokens.UserId!,
            ["target_id"] = animeId,
            ["target_type"] = "Anime",
            ["episodes"] = episodes
        };

        var shikiStatus = StatusMapper.ToShiki(status);
        if (!string.IsNullOrEmpty(shikiStatus)) userRate["status"] = shikiStatus;
        if (score.HasValue && score.Value > 0) userRate["score"] = score.Value;
        if (rewatchCount.HasValue) userRate["rewatches"] = rewatchCount.Value;

        var payload = new { user_rate = userRate };
        return await PostAsync("v2/user_rates", payload, ct);
    }

    public async Task<SyncOutcome> SaveFullListStatusAsync(AnimeItem item, CancellationToken ct = default)
    {
        return await UpdateProgressAsync(item.Id, item.Progress, item.Status, int.TryParse(item.Score, out var s) ? s : 0, item.IsRewatching, item.RewatchCount, ct);
    }

    public Task<List<AnimeItem>> SearchAnimeAsync(string query, CancellationToken ct = default)
    {
        return Task.FromResult(new List<AnimeItem>());
    }

    public Task<AnimeItem?> GetAnimeDetailsAsync(int animeId, CancellationToken ct = default)
    {
        return Task.FromResult<AnimeItem?>(null);
    }

    public Task<SyncOutcome> RemoveAnimeAsync(int animeId, CancellationToken ct = default)
    {
        // Shikimori deletes by user_rate_id, not anime_id. Until the service tracks
        // user_rate_id locally, treat remove as a no-op so SyncManager doesn't
        // endlessly retry and clutter history with SyncFailed entries.
        Log.Warning("ShikiApiService: remove is a no-op until user_rate_id is tracked locally ({AnimeId}).", animeId);
        return Task.FromResult(SyncOutcome.Success);
    }

    private async Task<int?> GetCurrentUserIdAsync(CancellationToken ct)
    {
        var response = await GetAsync("users/whoami", ct);
        if (!response.IsSuccessStatusCode) return null;

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, default, ct);
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + url.TrimStart('/'));
        return await SendRequestAsync(request, ct);
    }

    private async Task<SyncOutcome> PostAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, ShikiBaseUrl + url.TrimStart('/'))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        try
        {
            using var response = await SendRequestAsync(request, ct);
            var status = (int)response.StatusCode;
            if (status >= 200 && status < 300) return SyncOutcome.Success;
            if (status >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                Log.Warning("ShikiApiService: transient {Status} for POST {Uri}", status, request.RequestUri);
                return SyncOutcome.TransientFailure;
            }
            Log.Warning("ShikiApiService: permanent {Status} for POST {Uri}", status, request.RequestUri);
            return SyncOutcome.PermanentFailure;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "ShikiApiService: PostAsync failed ({Uri})", request.RequestUri);
            return SyncOutcome.TransientFailure;
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Add("User-Agent", AppInfo.UserAgent);
        var token = await EnsureValidTokenAsync(ct);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Routed through ShikiHttp so the .net ⇄ .rip geo-redirect / 404
        // dance is handled transparently with method+body+auth preserved.
        return await ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, ct);
    }

    private async Task<string?> EnsureValidTokenAsync(CancellationToken ct)
    {
        var tokens = _settingsService.Current.Api.Shiki;
        if (tokens == null) return null;
        if (!tokens.IsExpired) return tokens.AccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            tokens = _settingsService.Current.Api.Shiki;
            if (tokens == null || !tokens.IsExpired) return tokens?.AccessToken;

            var newTokens = await _authService.RefreshTokenAsync(tokens.RefreshToken, ct);
            if (newTokens != null)
            {
                newTokens.UserId = tokens.UserId;
                _settingsService.Update(settings => settings.Api.Shiki = newTokens, save: false);
                _settingsService.SaveImmediate();
                return newTokens.AccessToken;
            }
            return null;
        }
        finally { _tokenLock.Release(); }
    }
}
