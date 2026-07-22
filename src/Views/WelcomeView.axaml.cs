using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kiriha.Core;

namespace Kiriha.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        InitializeComponent();
        UpdateVersionLabel();
    }

    /// <summary>
    /// Composes the version line from the localized prefix
    /// (<c>l.common.version_prefix</c>) and the runtime build version
    /// (<see cref="AppInfo.Version"/>) so Welcome and About can never drift
    /// out of sync. Falls back gracefully if the resource is missing —
    /// still surfaces the actual version, which is the bit that matters.
    /// </summary>
    private void UpdateVersionLabel()
    {
        var prefix = Application.Current?.Resources["l.common.version_prefix"] as string ?? "Version";
        VersionLabel.Text = $"{prefix} {AppInfo.Version}";
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is Kiriha.ViewModels.WelcomeViewModel vm)
        {
            var owner = this.VisualRoot as Window;
            var dlg = new AboutWindow(vm.SettingsService);
            if (owner != null) dlg.ShowDialog(owner);
            else dlg.Show();
        }
    }
}
