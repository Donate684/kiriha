using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Kiriha.Views;

public partial class UpdateDialogView : UserControl
{
    public UpdateDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
