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
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.Seasonal;

public partial class SeasonalViewModel
{
    public async Task QuickAddToList(AnimeItem item, UserAnimeStatus status)
    {
        try
        {
            item.Status = status;
            await _animeService.AddOrUpdateAnimeAsync(item);
            await _syncManager.EnqueueUpdateAsync(item.Id, 0, status);
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SeasonalViewModel.QuickAddToList failed for {Title}", item.Title);
        }
    }
}
