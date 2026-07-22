using Avalonia.Controls;
using Avalonia.Interactivity;
using Kiriha.ViewModels;

namespace Kiriha.Views;

public partial class CrashReportWindow : KirihaWindowBase
{
    public CrashReportWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Whether the user clicked Close, the X, or used Alt+F4 — once they've dismissed
        // the window we treat the crash as seen and won't re-prompt on next launch.
        if (DataContext is CrashReportViewModel vm) vm.MarkSeen();
        base.OnClosing(e);
    }
}
