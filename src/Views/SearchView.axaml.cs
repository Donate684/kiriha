using System;
using Avalonia.Controls;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
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

namespace Kiriha.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }

    private void ListBox_ContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container.DataContext is Models.AnimeItem item && DataContext is SearchViewModel vm)
        {
            vm.EnqueueItemForViewport(item);
        }
    }

    private async void Poster_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        // async void event handler: any leaked exception kills the process. Wrap defensively.
        try
        {
            if (sender is Control c && c.DataContext is Models.AnimeItem item && DataContext is SearchViewModel vm)
            {
                await vm.DialogService.ShowAnimeDetailsAsync(this, item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "SearchView.Poster_DoubleTapped failed");
        }
    }
}
