using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;

namespace Kiriha.ViewModels;

#pragma warning disable MVVMTK0034

public partial class TorrentsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _onlyCrunchyroll;

    partial void OnOnlyCrunchyrollChanged(bool value) => PersistFilter(nameof(OnlyCrunchyroll), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterNetflix;

    partial void OnFilterNetflixChanged(bool value) => PersistFilter(nameof(FilterNetflix), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterAmazon;

    partial void OnFilterAmazonChanged(bool value) => PersistFilter(nameof(FilterAmazon), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterHidive;

    partial void OnFilterHidiveChanged(bool value) => PersistFilter(nameof(FilterHidive), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterVaryg;

    partial void OnFilterVarygChanged(bool value) => PersistFilter(nameof(FilterVaryg), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterEraiRaws;

    partial void OnFilterEraiRawsChanged(bool value) => PersistFilter(nameof(FilterEraiRaws), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterToonsHub;

    partial void OnFilterToonsHubChanged(bool value) => PersistFilter(nameof(FilterToonsHub), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filterHevc;

    partial void OnFilterHevcChanged(bool value) => PersistFilter(nameof(FilterHevc), value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveFilters))]
    private bool _filter1080p;

    partial void OnFilter1080pChanged(bool value) => PersistFilter(nameof(Filter1080p), value);

    [ObservableProperty]
    private bool _filtersPerTitle;

    /// <summary>Set during bulk-load of filter values so partial change handlers don't persist back.</summary>
    private bool _suppressFilterPersist;

    public bool HasActiveFilters =>
        FilterVaryg || FilterEraiRaws || FilterToonsHub || Filter1080p || FilterHevc
        || OnlyCrunchyroll || FilterNetflix || FilterAmazon || FilterHidive;

    partial void OnFiltersPerTitleChanged(bool value)
    {
        _settingsService.Update(settings => settings.Torrents.FiltersPerTitle = value);
        ReloadFiltersForCurrentContext();
    }

    private void LoadFilterSettings()
    {
        _onlyCrunchyroll = _settingsService.Current.Torrents.OnlyCrunchyroll;
        _filterNetflix = _settingsService.Current.Torrents.FilterNetflix;
        _filterAmazon = _settingsService.Current.Torrents.FilterAmazon;
        _filterHidive = _settingsService.Current.Torrents.FilterHidive;
        _filterVaryg = _settingsService.Current.Torrents.FilterVaryg;
        _filterEraiRaws = _settingsService.Current.Torrents.FilterEraiRaws;
        _filterToonsHub = _settingsService.Current.Torrents.FilterToonsHub;
        _filterHevc = _settingsService.Current.Torrents.FilterHevc;
        _filter1080p = _settingsService.Current.Torrents.Filter1080p;
        _filtersPerTitle = _settingsService.Current.Torrents.FiltersPerTitle;
    }

    private void PersistFilter(string name, bool value)
    {
        if (_suppressFilterPersist) return;

        _settingsService.Update(settings =>
        {
            var cfg = settings.Torrents;
            AppSettings.TorrentFilterSet target;
            if (FiltersPerTitle && SelectedAnime != null)
            {
                if (!cfg.PerTitleFilters.TryGetValue(SelectedAnime.Id, out target!))
                {
                    target = new AppSettings.TorrentFilterSet();
                    cfg.PerTitleFilters[SelectedAnime.Id] = target;
                }
            }
            else
            {
                target = CreateGlobalFilterSet(cfg);
            }

            ApplyFilterValue(target, name, value);

            if (!(FiltersPerTitle && SelectedAnime != null))
            {
                cfg.OnlyCrunchyroll = target.OnlyCrunchyroll;
                cfg.FilterNetflix = target.FilterNetflix;
                cfg.FilterAmazon = target.FilterAmazon;
                cfg.FilterHidive = target.FilterHidive;
                cfg.FilterVaryg = target.FilterVaryg;
                cfg.FilterEraiRaws = target.FilterEraiRaws;
                cfg.FilterToonsHub = target.FilterToonsHub;
                cfg.FilterHevc = target.FilterHevc;
                cfg.Filter1080p = target.Filter1080p;
            }
        });
        PerformSearchCommand.Execute(null);
    }

    private void ReloadFiltersForCurrentContext()
    {
        var cfg = _settingsService.Current.Torrents;
        AppSettings.TorrentFilterSet src;
        if (FiltersPerTitle && SelectedAnime != null && cfg.PerTitleFilters.TryGetValue(SelectedAnime.Id, out var saved))
        {
            src = saved;
        }
        else if (FiltersPerTitle && SelectedAnime != null)
        {
            src = new AppSettings.TorrentFilterSet();
        }
        else
        {
            src = CreateGlobalFilterSet(cfg);
        }

        _suppressFilterPersist = true;
        try
        {
            OnlyCrunchyroll = src.OnlyCrunchyroll;
            FilterNetflix = src.FilterNetflix;
            FilterAmazon = src.FilterAmazon;
            FilterHidive = src.FilterHidive;
            FilterVaryg = src.FilterVaryg;
            FilterEraiRaws = src.FilterEraiRaws;
            FilterToonsHub = src.FilterToonsHub;
            FilterHevc = src.FilterHevc;
            Filter1080p = src.Filter1080p;
        }
        finally
        {
            _suppressFilterPersist = false;
        }
    }

    private static AppSettings.TorrentFilterSet CreateGlobalFilterSet(AppSettings.TorrentConfig cfg) => new()
    {
        OnlyCrunchyroll = cfg.OnlyCrunchyroll,
        FilterNetflix = cfg.FilterNetflix,
        FilterAmazon = cfg.FilterAmazon,
        FilterHidive = cfg.FilterHidive,
        FilterVaryg = cfg.FilterVaryg,
        FilterEraiRaws = cfg.FilterEraiRaws,
        FilterToonsHub = cfg.FilterToonsHub,
        FilterHevc = cfg.FilterHevc,
        Filter1080p = cfg.Filter1080p,
    };

    private static void ApplyFilterValue(AppSettings.TorrentFilterSet target, string name, bool value)
    {
        switch (name)
        {
            case nameof(OnlyCrunchyroll): target.OnlyCrunchyroll = value; break;
            case nameof(FilterNetflix): target.FilterNetflix = value; break;
            case nameof(FilterAmazon): target.FilterAmazon = value; break;
            case nameof(FilterHidive): target.FilterHidive = value; break;
            case nameof(FilterVaryg): target.FilterVaryg = value; break;
            case nameof(FilterEraiRaws): target.FilterEraiRaws = value; break;
            case nameof(FilterToonsHub): target.FilterToonsHub = value; break;
            case nameof(FilterHevc): target.FilterHevc = value; break;
            case nameof(Filter1080p): target.Filter1080p = value; break;
        }
    }

    [RelayCommand]
    public void ClearFilters()
    {
        FilterVaryg = false;
        FilterEraiRaws = false;
        FilterToonsHub = false;
        Filter1080p = false;
        FilterHevc = false;
        OnlyCrunchyroll = false;
        FilterNetflix = false;
        FilterAmazon = false;
        FilterHidive = false;
    }
}

#pragma warning restore MVVMTK0034
