using System;
using System.Net;
using System.Net.Http;
using Kiriha.Core;
using Kiriha.Services;
using Kiriha.Services.Api;
using Kiriha.Services.Auth;
using Kiriha.Services.Data;
using Kiriha.Services.Data.Repositories;
using Kiriha.Services.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for the tracking layer: every service that talks to a
/// remote anime tracker (MyAnimeList, Shikimori, Jikan), the cross-tracker
/// orchestration around them (sync manager, scrobble pipeline, queues), and
/// background helpers that consume their state.
///
/// Each tracker is registered both under its concrete type (so call sites can
/// inject it directly when they need API-specific operations) and under
/// <see cref="ITrackerService"/> via a <c>sp.GetRequiredService</c> resolver
/// so <c>IEnumerable&lt;ITrackerService&gt;</c> consumers see the SAME singleton
/// instance Ã¢â‚¬â€ registering the implementation twice would create two parallel
/// instances and silently double the rate-limit budget.
/// </summary>
internal static class TrackingServicesRegistration
{
    public static IServiceCollection AddKirihaTracking(this IServiceCollection services)
    {
        services.AddTransient<ResilientHttpHandler>();

        // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬ MyAnimeList Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddHttpClient("MalClient", c => { c.BaseAddress = new Uri(Constants.Api.Mal.BaseUrl); })
                .AddHttpMessageHandler<ResilientHttpHandler>();

        services.AddSingleton<MalAuthService>(sp =>
            new MalAuthService(sp.GetRequiredService<IHttpClientFactory>().CreateClient("MalClient")));

        services.AddSingleton<MalApiService>(sp =>
            new MalApiService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("MalClient"),
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<MalAuthService>(),
                sp.GetRequiredService<JikanApiService>(),
                sp.GetRequiredService<IHttpCacheRepository>()));

        services.AddSingleton<ITrackerService>(sp => sp.GetRequiredService<MalApiService>());

        // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬ Shikimori Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        // No BaseAddress: ShikiApiService resolves the endpoint per-call from settings
        // (shikimori.one vs shikimori.net) and always passes absolute URLs.
        //
        // AllowAutoRedirect = false is critical: shikimori.net and shikimori.rip
        // are the same site behind two domains (regional-blocking workaround),
        // and the server geo-redirects between them. HttpClient's built-in
        // follower would (a) downgrade POST Ã¢â€ â€™ GET and drop the body, and
        // (b) strip the Authorization header on the cross-host hop. ShikiHttp
        // re-implements the follow with method/body/auth preserved and pins
        // the resolved host in ShikiHostResolver for the rest of the session.
        services.AddSingleton<ShikiHostResolver>();
        services.AddHttpClient("ShikiClient")
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.All,
                })
                .AddHttpMessageHandler<ResilientHttpHandler>();

        services.AddSingleton<ShikiAuthService>(sp =>
            new ShikiAuthService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ShikiClient"),
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<ShikiHostResolver>()));

        services.AddSingleton<ShikiApiService>(sp =>
            new ShikiApiService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("ShikiClient"),
                sp.GetRequiredService<SettingsService>(),
                sp.GetRequiredService<ShikiAuthService>(),
                sp.GetRequiredService<ShikiHostResolver>()));

        services.AddSingleton<ITrackerService>(sp => sp.GetRequiredService<ShikiApiService>());

        // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬ Jikan / AniList Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddSingleton<JikanApiService>();
        services.AddHttpClient("AniListClient")
                .AddHttpMessageHandler<ResilientHttpHandler>();
        services.AddSingleton<AniListApiService>(sp =>
            new AniListApiService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("AniListClient"),
                sp.GetRequiredService<IHttpCacheRepository>()));

        // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬ RSS Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddHttpClient("RssClient", c => c.DefaultRequestHeaders.Add("User-Agent", AppInfo.UserAgent));
        services.AddSingleton<RssFeedService>();

        // Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬ Cross-tracker orchestration Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddSingleton<SmtcService>(sp => new SmtcService(sp.GetRequiredService<SettingsService>()));
        services.AddSingleton<DiscordService>();
        services.AddSingleton<AnisthesiaService>();
        services.AddSingleton<IScrobbleService, ScrobbleService>();
        services.AddSingleton<TrackingService>();
        services.AddSingleton<AnimeService>();
        services.AddSingleton<SyncManager>();
        services.AddSingleton<InternalPlayerServer>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => sp.GetRequiredService<InternalPlayerServer>());
        
        services.AddSingleton<InstanceServer>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => sp.GetRequiredService<InstanceServer>());
        
        // Same instance, second registration: lets the app lifecycle resolve every
        // IHostedService and Start/Stop them uniformly without naming each service.
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => sp.GetRequiredService<SyncManager>());
        services.AddSingleton<LoadQueueService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AiringInfoService>();
        services.AddSingleton<MaintenanceService>();

        return services;
    }
}
