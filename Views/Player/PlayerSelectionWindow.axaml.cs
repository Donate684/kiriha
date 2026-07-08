using Kiriha.Views.Player;
using Kiriha.Views.AnimeList;
using Avalonia.Controls;
using Kiriha.Services;
using Kiriha.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Views.Player;

public partial class PlayerSelectionWindow : Window
{
    public PlayerSelectionWindow()
    {
        InitializeComponent();
        ApplyMica();
    }

    public void ApplyMica()
    {
        var settings = App.Services.GetRequiredService<SettingsService>().Current;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur };
            Background = null;
        }
        else
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            ClearValue(BackgroundProperty);
        }
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }
}
