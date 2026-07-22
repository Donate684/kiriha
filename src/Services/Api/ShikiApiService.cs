using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Models.Entities;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Api;

public partial class ShikiApiService : ITrackerService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ShikiAuthService _authService;
    private readonly ShikiHostResolver _hostResolver;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly HttpConditionalCache _httpCache;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (ShikiPersonResponse? Value, DateTime SystemDateTime)> _personCache = new();

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

    public ShikiApiService(HttpClient httpClient, SettingsService settingsService, ShikiAuthService authService, ShikiHostResolver hostResolver, Kiriha.Services.Data.Repositories.IHttpCacheRepository httpCacheRepo)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _authService = authService;
        _hostResolver = hostResolver;
        _httpCache = new HttpConditionalCache(
            _httpClient,
            httpCacheRepo,
            "ShikiApi",
            (client, request, innerCt) => SendRequestAsync(request, innerCt));
    }


    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ShikiBaseUrl + url.TrimStart('/'));
        return await SendRequestAsync(request, ct);
    }

    private async Task<SyncOutcome> PostAsync(string url, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, ShikiBaseUrl + url.TrimStart('/'))
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
                _settingsService.Update(settings => settings.Api.Shiki = newTokens, SettingsSection.Api, save: false);
                _settingsService.SaveImmediate();
                return newTokens.AccessToken;
            }
            return null;
        }
        finally { _tokenLock.Release(); }
    }
}
