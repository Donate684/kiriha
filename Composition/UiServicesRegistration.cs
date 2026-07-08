using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
using Kiriha.Core.Dialogs;
using Kiriha.Core.Navigation;
using Kiriha.ViewModels;
using Kiriha.ViewModels.Analytics;
using Kiriha.ViewModels.AnimeDetails;
using Kiriha.ViewModels.AnimeList;
using Kiriha.ViewModels.History;
using Kiriha.ViewModels.Player;
using Kiriha.ViewModels.Seasonal;
using Kiriha.ViewModels.Settings;
using Kiriha.ViewModels.Torrents;
using Kiriha.ViewModels.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Composition;

/// <summary>
/// DI registrations for the UI layer: all ViewModels (singleton vs transient
/// lifetime decisions are deliberate and documented inline), plus the abstract
/// services they consume — <see cref="IDialogService"/> and
/// <see cref="IViewModelFactory"/>.
///
/// Lifetime rationale:
///   * <c>Singleton</c> for VMs whose state outlives a single navigation
///     (anime list, settings, history, seasonal, now-playing, torrents, main).
///   * <c>Transient</c> for VMs that should reset on every open: WelcomeView
///     animations, SearchViewModel's per-query state, FirstStartupViewModel.
/// </summary>
internal static class UiServicesRegistration
{
    public static IServiceCollection AddKirihaUi(this IServiceCollection services)
    {
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();

        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AnimeListViewModel>();
        services.AddSingleton<SeasonalViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TorrentsViewModel>();
        services.AddSingleton<AnalyticsViewModel>();

        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<FirstStartupViewModel>();

        return services;
    }
}
