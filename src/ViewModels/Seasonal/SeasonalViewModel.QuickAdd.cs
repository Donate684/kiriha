using System;
using System.Threading.Tasks;
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
            await _animeRepo.AddOrUpdateAnimeAsync(item);
            await _syncManager.EnqueueUpdateAsync(item.Id, 0, status);
            WeakReferenceMessenger.Default.Send(new AnimeListRefreshMessage());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SeasonalViewModel.QuickAddToList failed for {Title}", item.Title);
        }
    }
}
