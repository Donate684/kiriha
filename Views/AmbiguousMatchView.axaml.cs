using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Kiriha.ViewModels;

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
