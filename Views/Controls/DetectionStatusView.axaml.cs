using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kiriha.Views.Controls;

public partial class DetectionStatusView : UserControl
{
    public DetectionStatusView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
