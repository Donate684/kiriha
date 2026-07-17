using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking;
using Serilog;

namespace Kiriha.ViewModels.Settings;

public partial class SettingsViewModel
{
    private readonly Dictionary<CustomShareLink, CancellationTokenSource> _faviconDebouncers = new();

    private void InitializeCustomLinks()
    {
        foreach (var link in _settingsService.Current.CustomLinks)
        {
            HookCustomLink(link);
            CustomLinks.Add(link);

            // Backfill icon for legacy links that were saved before the
            // auto-favicon feature, or whose cached file got cleaned up.
            if (string.IsNullOrWhiteSpace(link.IconPath) || !System.IO.File.Exists(link.IconPath))
            {
                ScheduleFaviconFetch(link, delayMs: 0);
            }
        }
        CustomLinks.CollectionChanged += OnCustomLinksCollectionChanged;
    }

    private void HookCustomLink(CustomShareLink link)
    {
        link.PropertyChanged += OnCustomLinkPropertyChanged;
    }

    private void UnhookCustomLink(CustomShareLink link)
    {
        link.PropertyChanged -= OnCustomLinkPropertyChanged;
        if (_faviconDebouncers.Remove(link, out var cts))
        {
            try { cts.Cancel(); } catch { /* ignore */ }
            cts.Dispose();
        }
    }

    private void OnCustomLinkPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // User typed something into Name / UrlTemplate — persist.
        _settingsService.Update(settings => { }, SettingsSection.CustomLinks, save: false);
        _settingsService.Save();

        // When the URL changes, try to refresh the favicon. We deliberately
        // do NOT trigger on IconPath changes (that's the destination of this
        // very fetch and would loop).
        if (sender is CustomShareLink link && e.PropertyName == nameof(CustomShareLink.UrlTemplate))
        {
            ScheduleFaviconFetch(link);
        }
    }

    /// <summary>
    /// Debounced favicon fetch for a single link. Cancels any in-flight
    /// fetch for the same link, waits a short delay (so we don't hammer
    /// hosts while the user is still typing), then assigns the result to
    /// <see cref="CustomShareLink.IconPath"/> on the UI thread. On failure
    /// the IconPath is left unchanged and the UI falls back to the globe.
    /// </summary>
    private void ScheduleFaviconFetch(CustomShareLink link, int delayMs = 700)
    {
        if (_faviconDebouncers.Remove(link, out var prev))
        {
            try { prev.Cancel(); } catch { /* ignore */ }
            prev.Dispose();
        }

        var cts = new CancellationTokenSource();
        _faviconDebouncers[link] = cts;
        var token = cts.Token;
        var template = link.UrlTemplate;

        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMs > 0) await Task.Delay(delayMs, token);
                var path = await _faviconService.TryGetFaviconAsync(template, token);
                if (token.IsCancellationRequested) return;
                if (string.IsNullOrEmpty(path)) return;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // The user may have edited the URL again in between;
                    // only apply if the URL still matches what we fetched
                    // for, otherwise a newer scheduled fetch will replace it.
                    if (link.UrlTemplate == template)
                    {
                        link.IconPath = path;
                    }
                });
            }
            catch (OperationCanceledException) { /* expected on debounce */ }
            catch (Exception ex)
            {
                Log.Debug(ex, "Favicon scheduling failed for {Url}", template);
            }
        }, token);
    }

    private void OnCustomLinksCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (CustomShareLink old in e.OldItems) UnhookCustomLink(old);
        if (e.NewItems != null)
            foreach (CustomShareLink added in e.NewItems) HookCustomLink(added);

        // Mirror the ObservableCollection back into AppSettings (single source of truth on disk).
        _settingsService.Update(settings =>
        {
            settings.CustomLinks.Clear();
            foreach (var link in CustomLinks) settings.CustomLinks.Add(link);
        }, SettingsSection.CustomLinks);
    }

    [RelayCommand]
    private void AddCustomLink()
    {
        CustomLinks.Add(new CustomShareLink
        {
            Name = string.Empty,
            UrlTemplate = string.Empty,
            IconKind = "Web"
        });
    }

    /// <summary>
    /// Adds a preset custom link. Parameter is a "Name|Url" string defined
    /// in XAML so each preset button is just a different CommandParameter.
    /// The favicon is then auto-fetched via the usual UrlTemplate hook.
    /// </summary>
    [RelayCommand]
    private void AddPresetLink(string? preset)
    {
        if (string.IsNullOrWhiteSpace(preset)) return;
        var parts = preset.Split('|', 2);
        if (parts.Length != 2) return;

        var link = new CustomShareLink
        {
            Name = parts[0],
            UrlTemplate = parts[1],
            IconKind = "Web"
        };
        CustomLinks.Add(link);

        // The UrlTemplate is set in the object initializer BEFORE the link
        // is hooked into the collection's PropertyChanged pipeline, so the
        // usual auto-favicon hook never fires for presets. Trigger it
        // manually here (no debounce — the URL is final).
        ScheduleFaviconFetch(link, delayMs: 0);
    }

    [RelayCommand]
    private void RemoveCustomLink(CustomShareLink? link)
    {
        if (link == null) return;
        // Don't delete the cached favicon: it's keyed by host and may be
        // shared with other links pointing at the same site. The cache
        // directory holds tiny files and is self-healing on re-fetch.
        CustomLinks.Remove(link);
    }

}
