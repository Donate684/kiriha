using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class StaffWorkVm : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _score = string.Empty;

    [ObservableProperty]
    private Avalonia.Media.IBrush _highlightColor = Avalonia.Media.Brushes.Transparent;
}
