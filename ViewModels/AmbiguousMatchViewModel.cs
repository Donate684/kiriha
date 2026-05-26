using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models.Entities;
using Kiriha.Services.Api;
using Kiriha.Services.Data;
using Serilog;

namespace Kiriha.ViewModels;

public partial class AmbiguousMatchViewModel : ViewModelBase
{
    private readonly MalApiService _malApi;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    public ObservableCollection<AnimeOfflineItem> Candidates { get; } = new();

    [ObservableProperty]
    private AnimeOfflineItem? _selectedAnime;

    public AmbiguousMatchViewModel(string fileName, IEnumerable<AnimeOfflineItem> candidates, MalApiService malApi)
    {
        _fileName = fileName;
        _malApi = malApi;
        
        foreach (var c in candidates) Candidates.Add(c);
        SelectedAnime = Candidates.FirstOrDefault();
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;

        IsSearching = true;
        try
        {
            var results = await _malApi.SearchAnimeAsync(SearchQuery);
            Candidates.Clear();
            
            foreach (var r in results)
            {
                Candidates.Add(new AnimeOfflineItem
                {
                    Id = r.Id,
                    Title = r.Title,
                    Type = r.Type,
                    Year = r.StartYear,
                    Season = r.StartSeason ?? "",
                    ImageUrl = r.MainPictureUrl,
                    TotalEpisodes = r.TotalEpisodes
                });
            }
            
            SelectedAnime = Candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to search anime in manual match dialog");
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void Confirm(object? window)
    {
        if (window is Avalonia.Controls.Window w)
        {
            w.Close(SelectedAnime?.Id);
        }
    }

    [RelayCommand]
    private void Cancel(object? window)
    {
        if (window is Avalonia.Controls.Window w)
        {
            w.Close(null);
        }
    }
}
