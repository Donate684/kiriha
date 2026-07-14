using Avalonia.Controls;

namespace Kiriha.Views;

public partial class FirstStartupView : UserControl
{
    public FirstStartupView()
    {
        InitializeComponent();
    }

    protected override void OnPointerPressed(Avalonia.Input.PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (VisualRoot is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }
}
