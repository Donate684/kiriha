using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Kiriha.Models;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
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
using Serilog;

namespace Kiriha.Core.Dialogs;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>. Owns the rules for
/// picking an owner window, deferring dialog opening until the main window is
/// visible (so a dialog doesn't materialise behind a hidden / minimised root),
/// and resolving dependencies for the dialog ViewModels via DI rather than the
/// static <c>App.GetService</c> locator.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly IServiceProvider _services;

    public AvaloniaDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public async Task<bool> ShowAnimeDetailsAsync(Control? sourceControl, AnimeItem item, CancellationToken ct = default)
    {
        var owner = ResolveOwner(sourceControl);
        if (owner == null) return false;

        // Resolve dependencies via DI scope. Note the dialog's VM is currently
        // not registered in the container (it carries a load of per-call state),
        // so we new it up explicitly with services pulled from the provider.
        var vm = new AnimeDetailsViewModel(
            item,
            _services.GetRequiredService<MalApiService>(),
            _services.GetRequiredService<ShikiApiService>(),
            _services.GetRequiredService<JikanApiService>(),
            _services.GetRequiredService<SyncManager>(),
            _services.GetRequiredService<AnimeService>(),
            _services.GetRequiredService<AiringInfoService>(),
            _services.GetRequiredService<SettingsService>(),
            _services.GetRequiredService<HistoryService>(),
            this);

        var window = new Views.AnimeDetailsWindow { DataContext = vm };

        try
        {
            await WaitForVisibleAsync(owner, ct);
            var result = await window.ShowDialog<bool?>(owner);
            return result == true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public async Task ShowUpdateDialogAsync(bool isDownloaded = false, CancellationToken ct = default)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null)
            return;

        try
        {
            await WaitForVisibleAsync(desktop.MainWindow, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (desktop.MainWindow.DataContext is MainWindowViewModel mainVm)
            mainVm.ShowUpdateDialog(isDownloaded);
    }

    /// <summary>
    /// Picks the window that should own a dialog: prefer the top-level of the
    /// triggering control (so the dialog is centred over the actual surface
    /// the user clicked on), fall back to the desktop main window.
    /// </summary>
    private static Window? ResolveOwner(Control? source)
    {
        if (source != null && TopLevel.GetTopLevel(source) is Window w) return w;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    /// <summary>
    /// Suspends until <paramref name="window"/> is both visible and not minimised.
    /// Throws <see cref="OperationCanceledException"/> if the window is closed
    /// before becoming visible, or if <paramref name="ct"/> fires.
    /// Implemented with strongly-typed property comparison (no string lookup).
    /// </summary>
    private static Task WaitForVisibleAsync(Window window, CancellationToken ct)
    {
        if (window.IsVisible && window.WindowState != WindowState.Minimized)
            return Task.CompletedTask;

        Log.Information("DialogService: main window is hidden/minimised, deferring dialog until visible");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        EventHandler<AvaloniaPropertyChangedEventArgs>? propertyHandler = null;
        EventHandler<WindowClosingEventArgs>? closingHandler = null;
        CancellationTokenRegistration ctRegistration = default;

        void Cleanup()
        {
            if (propertyHandler != null) window.PropertyChanged -= propertyHandler;
            if (closingHandler != null) window.Closing -= closingHandler;
            ctRegistration.Dispose();
        }

        propertyHandler = (_, args) =>
        {
            // Strongly-typed compare: avoids the reflection-y string lookup that
            // the old UIUtils used and survives Avalonia property renames.
            if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty)
            {
                if (window.IsVisible && window.WindowState != WindowState.Minimized)
                {
                    Cleanup();
                    tcs.TrySetResult(true);
                }
            }
        };
        closingHandler = (_, _) =>
        {
            Cleanup();
            tcs.TrySetException(new OperationCanceledException("Main window closed before becoming visible"));
        };

        window.PropertyChanged += propertyHandler;
        window.Closing += closingHandler;

        if (ct.CanBeCanceled)
        {
            ctRegistration = ct.Register(() =>
            {
                Cleanup();
                tcs.TrySetCanceled(ct);
            });
        }

        return tcs.Task;
    }
}
