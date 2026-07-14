using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using Serilog;
using Kiriha.Core.Dialogs;
using Kiriha.Models;

namespace Kiriha.ViewModels;

public partial class FranchiseGraphViewModel : ViewModelBase
{
    private readonly ShikiApiService _shikiApi;
    private readonly IDialogService _dialogs;
    private readonly int _baseAnimeId;

    [ObservableProperty]
    private FranchiseGraphLayout? _layout;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public FranchiseGraphViewModel(int animeId, ShikiApiService shikiApi, IDialogService dialogs)
    {
        _baseAnimeId = animeId;
        _shikiApi = shikiApi;
        _dialogs = dialogs;
    }

    public async Task LoadGraphAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var data = await _shikiApi.GetFranchiseAsync(_baseAnimeId);
            if (data != null && data.Nodes.Count > 0)
            {
                // Assign currentId if the API doesn't do it reliably
                if (data.CurrentId == 0) data.CurrentId = _baseAnimeId;
                
                Layout = FranchiseLayoutEngine.CalculateLayout(data);
            }
            else
            {
                ErrorMessage = "Не удалось загрузить данные франшизы или франшиза пуста.";
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Failed to load franchise graph for {AnimeId}", _baseAnimeId);
            ErrorMessage = "Произошла ошибка при загрузке графа.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task NodeClicked(FranchiseGraphVisualNode node)
    {
        if (node == null || node.Node == null) return;
        
        MediaKind kind = node.Node.Kind.ToLowerInvariant() switch
        {
            "manga" or "manhwa" or "manhua" or "one_shot" or "doujin" => MediaKind.Manga,
            "novel" or "light_novel" => MediaKind.LightNovel,
            _ => MediaKind.Anime
        };
        
        var targetAnime = new AnimeItem
        {
            Id = node.Node.Id,
            Title = node.Node.Name,
            MediaKind = kind,
            MainPictureUrl = node.Node.ImageUrl
        };
        
        await _dialogs.ShowAnimeDetailsAsync(null, targetAnime);
    }
}
