using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kiriha.Views.Controls.Variants;

public partial class NowPlayingCompact : UserControl
{
    public NowPlayingCompact()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ShareRow_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ShareMainButton");
        btn?.Flyout?.Hide();
    }
}
