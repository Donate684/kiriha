using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kiriha.ViewModels;
using Kiriha.Views;

namespace Kiriha.Core;

/// <summary>
/// Resolves a <see cref="ViewModelBase"/> to its companion <see cref="Control"/>.
///
/// The previous implementation derived the view type name from the VM type name
/// via reflection (<c>Type.GetType(name.Replace("ViewModel", "View"))</c>). That
/// pulled <see cref="System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"/>
/// into every consumer, broke under trimming, and silently fell back to a
/// "Not Found" TextBlock if anyone renamed a view without renaming its VM.
///
/// The explicit map below is compile-time validated, AOT-/trim-safe, and gives
/// a single grep target for "which VMs need DataTemplate routing". VMs that are
/// only ever shown via a directly-constructed <see cref="Window"/> (e.g.
/// <see cref="AnimeDetailsViewModel"/>, <see cref="CrashReportViewModel"/>,
/// <see cref="MainWindowViewModel"/>, <see cref="PlayerSelectionViewModel"/>)
/// are intentionally absent Ã¢â‚¬â€ they don't participate in DataTemplate routing.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Map = new()
    {
        [typeof(AmbiguousMatchViewModel)] = () => new AmbiguousMatchView(),
        [typeof(AnimeListViewModel)] = () => new AnimeListView(),
        [typeof(AnalyticsViewModel)] = () => new AnalyticsView(),
        [typeof(FirstStartupViewModel)] = () => new FirstStartupView(),
        [typeof(HistoryViewModel)] = () => new HistoryView(),
        [typeof(NowPlayingViewModel)] = () => new NowPlayingView(),
        [typeof(SearchViewModel)] = () => new SearchView(),
        [typeof(SeasonalViewModel)] = () => new SeasonalView(),
        [typeof(SettingsViewModel)] = () => new SettingsView(),
        [typeof(TorrentsViewModel)] = () => new TorrentsView(),
        [typeof(UpdateDialogViewModel)] = () => new UpdateDialogView(),
        [typeof(WelcomeViewModel)] = () => new WelcomeView(),
    };

    public Control? Build(object? param)
    {
        if (param is null) return null;
        if (Map.TryGetValue(param.GetType(), out var factory)) return factory();
        // Fall back to a visible diagnostic instead of throwing Ã¢â‚¬â€ keeping
        // parity with the old reflective behaviour so a missed registration
        // doesn't crash the shell.
        return new TextBlock { Text = "ViewLocator: no view registered for " + param.GetType().FullName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
