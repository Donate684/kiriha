using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models.Api;
using Serilog;

namespace Kiriha.Services.Auth;

public class MalAuthService
{
    private readonly HttpClient _httpClient;

    public MalAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string CreateCodeVerifier() => GenerateCodeVerifier();

    public string GetAuthUrl(string codeVerifier)
    {
        return $"{Constants.Api.Mal.AuthUrl}?response_type=code&client_id={ApiKeys.MalClientId}&code_challenge={codeVerifier}&redirect_uri={Constants.Api.RedirectUri}&scope=write:users";
    }

    public async Task<MalTokens?> LoginWithCodeAsync(string code, string codeVerifier)
    {
        return await ExchangeCodeForTokenAsync(code, codeVerifier);
    }

    public async Task<MalTokens?> LoginAsync()
    {
        var codeVerifier = GenerateCodeVerifier();
        var authUrl = GetAuthUrl(codeVerifier);
        string successMessage = UIUtils.GetLoc("auth.success", "MyAnimeList");
        var code = await OAuthHelper.AuthorizeViaLoopbackAsync(authUrl, Constants.Api.RedirectUri, successMessage);

        if (string.IsNullOrEmpty(code)) return null;

        return await ExchangeCodeForTokenAsync(code, codeVerifier);
    }

    /// <summary>
    /// Refreshes an expired access token using the refresh token.
    /// Returns new tokens or null if refresh failed.
    /// </summary>
    public async Task<MalTokens?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        Log.Information("Refreshing MAL access token...");

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", ApiKeys.MalClientId),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        try
        {
            var response = await _httpClient.PostAsync(Constants.Api.Mal.TokenUrl, content, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var tokens = JsonSerializer.Deserialize<MalTokens>(json);
                if (tokens != null) tokens.CreatedAt = DateTime.UtcNow;
                Log.Information("MAL token refreshed successfully.");
                return tokens;
            }

            Log.Error("Failed to refresh MAL token. Status: {StatusCode}, Body: {Body}",
                response.StatusCode, json.Length > 200 ? json[..200] : json);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception while refreshing MAL token");
            return null;
        }
    }

    private async Task<MalTokens?> ExchangeCodeForTokenAsync(string code, string codeVerifier)
    {
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", ApiKeys.MalClientId),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("code_verifier", codeVerifier),
            new KeyValuePair<string, string>("redirect_uri", Constants.Api.RedirectUri)
        });

        var response = await _httpClient.PostAsync(Constants.Api.Mal.TokenUrl, content);
        var json = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            var tokens = JsonSerializer.Deserialize<MalTokens>(json);
            if (tokens != null) tokens.CreatedAt = DateTime.UtcNow;
            return tokens;
        }

        Log.Error("?????? ?????? ???? ?? ?????: {Response}", json);
        return null;
    }

    private string GenerateCodeVerifier()
    {
        var randomBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

