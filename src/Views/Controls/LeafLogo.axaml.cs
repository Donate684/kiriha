using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kiriha.Views.Controls;

public partial class LeafLogo : UserControl
{
    public static readonly StyledProperty<bool> ShowHaloProperty =
        AvaloniaProperty.Register<LeafLogo, bool>(nameof(ShowHalo), defaultValue: true);

    public bool ShowHalo
    {
        get => GetValue(ShowHaloProperty);
        set => SetValue(ShowHaloProperty, value);
    }

    public LeafLogo()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
