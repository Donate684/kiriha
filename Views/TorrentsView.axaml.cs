using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kiriha.Views;

public partial class TorrentsView : UserControl
{
    public TorrentsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
