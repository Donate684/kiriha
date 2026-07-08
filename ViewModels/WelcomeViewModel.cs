using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
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
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels;

public partial class WelcomeViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty]
    private bool _isLoading = true;

    private readonly AnimeListViewModel _animeListViewModel;

    public WelcomeViewModel(AnimeListViewModel animeListViewModel)
    {
        _animeListViewModel = animeListViewModel;
        _isLoading = _animeListViewModel.IsBusy;
        _animeListViewModel.PropertyChanged += OnAnimeListPropertyChanged;
    }

    private void OnAnimeListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnimeListViewModel.IsBusy))
        {
            IsLoading = _animeListViewModel.IsBusy;
        }
    }

    public void Dispose()
    {
        if (_animeListViewModel != null)
        {
            _animeListViewModel.PropertyChanged -= OnAnimeListPropertyChanged;
        }
    }
}
