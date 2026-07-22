using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core.Shiki;
using Kiriha.Models.Api;
using Kiriha.Models;
using Serilog;

namespace Kiriha.Services.Auth;

public partial class ShikiAuthService
{
    private async Task<ShikiTokens?> ExchangeCodeForTokenAsync(string code, ShikiMirror mirror)
    {
        var values = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", ShikiEndpoints.ClientId(mirror) },
            { "code", code },
            { "redirect_uri", Kiriha.Core.Constants.Api.RedirectUri }
        };
        // Confidential OAuth app: Doorkeeper requires client_secret on every token call.
        // See ApiKeys.cs for why these are embedded instead of proxied.
        var clientSecret = ShikiEndpoints.ClientSecret(mirror);
        if (!string.IsNullOrEmpty(clientSecret)) values["client_secret"] = clientSecret;

        var content = new FormUrlEncodedContent(values);

        var request = new HttpRequestMessage(HttpMethod.Post, ShikiEndpoints.TokenUrl(mirror)) { Content = content };
        request.Headers.Add("User-Agent", Kiriha.Core.AppInfo.UserAgent);

        try
        {
            // ShikiHttp transparently follows the .net ⇄ .rip geo-redirect
            // while preserving the POST body — without it, the form fields
            // would be dropped and the server would return an empty token
            // payload, surfacing as "login succeeded but app stays empty".
            var response = await Kiriha.Services.Api.ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, CancellationToken.None);
            var json = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var tokens = JsonSerializer.Deserialize<ShikiTokens>(json);
                if (tokens != null) { tokens.CreatedAt = DateTime.UtcNow; tokens.Mirror = mirror; }
                return tokens;
            }

            Log.Error("Shikimori token exchange failed ({Mirror}). Status: {StatusCode}, Response: {Body}", mirror, response.StatusCode, json);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during Shikimori token exchange ({Mirror})", mirror);
            return null;
        }
    }

    public async Task<ShikiTokens?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        // Always refresh against the mirror that issued the existing token. If the saved
        // token has no mirror stamp (legacy data), fall back to the active mirror.
        var savedMirror = _settingsService.Current.Api.Shiki?.Mirror ?? ActiveMirror;

        var values = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "client_id", ShikiEndpoints.ClientId(savedMirror) },
            { "refresh_token", refreshToken }
        };
        var clientSecret = ShikiEndpoints.ClientSecret(savedMirror);
        if (!string.IsNullOrEmpty(clientSecret)) values["client_secret"] = clientSecret;

        var content = new FormUrlEncodedContent(values);
        var request = new HttpRequestMessage(HttpMethod.Post, ShikiEndpoints.TokenUrl(savedMirror)) { Content = content };
        request.Headers.Add("User-Agent", Kiriha.Core.AppInfo.UserAgent);

        try
        {
            var response = await Kiriha.Services.Api.ShikiHttp.SendShikiAsync(_httpClient, request, _hostResolver, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var tokens = JsonSerializer.Deserialize<ShikiTokens>(json);
                if (tokens != null) { tokens.CreatedAt = DateTime.UtcNow; tokens.Mirror = savedMirror; }
                return tokens;
            }

            Log.Error("Failed to refresh Shikimori token ({Mirror}): {Body}", savedMirror, json);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during Shikimori token refresh ({Mirror})", savedMirror);
            return null;
        }
    }
}
