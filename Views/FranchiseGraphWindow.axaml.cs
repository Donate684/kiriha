using Avalonia.Controls;
using Avalonia.Input;
using Kiriha.ViewModels;
using System;

namespace Kiriha.Views;

public partial class FranchiseGraphWindow : Window
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
