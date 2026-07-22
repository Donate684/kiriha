using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Models.Api;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.Services.Auth;

public partial class ShikiAuthService
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly ShikiHostResolver _hostResolver;

    private ShikiMirror ActiveMirror => _settingsService.Current.Api.ShikiMirror;
    private string ClientId => ShikiEndpoints.ClientId(ActiveMirror);
    private string TokenUrl => ShikiEndpoints.TokenUrl(ActiveMirror);
    private string AuthBase => ShikiEndpoints.AuthUrl(ActiveMirror);

    public ShikiAuthService(HttpClient httpClient, SettingsService settingsService, ShikiHostResolver hostResolver)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
        _hostResolver = hostResolver;
    }

    public string GetAuthUrl()
    {
        // Shikimori redirect URI must match exactly what's in the application settings on Shikimori website
        return $"{AuthBase}?client_id={ClientId}&redirect_uri={Constants.Api.RedirectUri}&response_type=code&scope=user_rates";
    }

    public async Task<ShikiTokens?> LoginAsync()
    {
        if (!ShikiEndpoints.IsConfigured(ActiveMirror))
        {
            Log.Error("Shikimori OAuth is not configured for mirror {Mirror}. Set ClientId/TokenUrl first.", ActiveMirror);
            return null;
        }

        var mirror = ActiveMirror;
        var authUrl = GetAuthUrl();
        string successMessage = UIUtils.GetLoc("auth.success", "Shikimori");
        var code = await OAuthHelper.AuthorizeViaLoopbackAsync(authUrl, Constants.Api.RedirectUri, successMessage);

        if (string.IsNullOrEmpty(code)) return null;

        var tokens = await ExchangeCodeForTokenAsync(code, mirror);
        if (tokens != null) tokens.Mirror = mirror;
        return tokens;
    }


}
