using Avalonia.Controls;

namespace Kiriha.Views.Controls.Variants;

public partial class SettingsSidebar : UserControl
{
    // Re-entrancy guard: clearing the "other" ListBox's SelectedIndex itself
    // raises SelectionChanged, which would then try to clear the ListBox the
    // user just clicked. The flag short-circuits the second event so only the
    // user's chosen item ends up selected.
    private bool _suppressNavSync;

    public SettingsSidebar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// User picked something in the main "Система" navigation — make sure the
    /// "Расширенная кастомизация" group has nothing selected so its associated
    /// page hides and only the main page is visible.
    /// </summary>
    private void Nav_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Nav.EndInit fires SelectionChanged before NavCustom has been
        // populated by the XAML loader, so guard against null here AND
        // against the suppress flag during programmatic clearing.
        if (_suppressNavSync) return;
        if (NavCustom == null) return;
        if (Nav.SelectedIndex < 0) return;

        _suppressNavSync = true;
        try { NavCustom.SelectedIndex = -1; }
        finally { _suppressNavSync = false; }
    }

    /// <summary>
    /// Inverse of <see cref="Nav_SelectionChanged"/>: when something in the
    /// custom-links group is clicked, clear the main nav's selection so only
    /// the custom-links page shows.
    /// </summary>
    private void NavCustom_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSync) return;
        if (Nav == null) return;
        if (NavCustom.SelectedIndex < 0) return;

        _suppressNavSync = true;
        try { Nav.SelectedIndex = -1; }
        finally { _suppressNavSync = false; }
    }
}
