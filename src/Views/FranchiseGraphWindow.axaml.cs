using System;
using Avalonia.Input;
using Kiriha.ViewModels;

namespace Kiriha.Views;

public partial class FranchiseGraphWindow : KirihaWindowBase
{
    public FranchiseGraphWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is FranchiseGraphViewModel vm)
        {
            await vm.LoadGraphAsync();
        }
    }
}
