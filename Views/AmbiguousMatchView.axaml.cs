using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
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

namespace Kiriha.Views;

public partial class AmbiguousMatchView : Window
{
    public AmbiguousMatchView()
    {
        InitializeComponent();
        
        var listBox = this.FindControl<ListBox>("CandidatesList");
        if (listBox != null)
        {
            listBox.DoubleTapped += (s, e) => {
                var vm = DataContext as AmbiguousMatchViewModel;
                if (vm?.SelectedAnime != null)
                {
                    vm.ConfirmCommand.Execute(this);
                }
            };
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
