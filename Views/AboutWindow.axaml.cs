using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kiriha.Core;
using Kiriha.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Kiriha.Views;

public partial class AboutWindow : Window
{
    /// <summary>
    /// Public APIs we consume. Names match the user-facing service identity,
    /// not the SDK package — these power the data shown in the app and are
    /// surfaced separately from the .NET library credits below.
    /// </summary>
    public IReadOnlyList<CreditEntry> DataSources { get; } = new[]
    {
        new CreditEntry("MyAnimeList", "Account, list, scoring · myanimelist.net",  "https://myanimelist.net"),
        new CreditEntry("AniList",     "Episode airing schedule · anilist.co",      "https://anilist.co"),
        new CreditEntry("Shikimori",   "Russian titles & descriptions · shikimori.one", "https://shikimori.one"),
        new CreditEntry("ShikimoriRIP", "Community fork Shikimori · shikimori.rip", "https://shikimori.rip"),
        new CreditEntry("Nyaa.si",     "Torrent feed · nyaa.si",                    "https://nyaa.si"),
    };

    public IReadOnlyList<CreditEntry> Inspirations { get; } = new[]
    {
        new CreditEntry("MAL Updater",       "Inspiration · malupdater.com", "https://malupdater.com/"),
        new CreditEntry("Taiga",             "Inspiration · erengy/taiga",   "https://github.com/erengy/taiga"),
    };

    /// <summary>
    /// Static credit list — kept in code-behind (not VM/JSON) because it
    /// rarely changes and binding it to a service would be overkill. Names
    /// must match the actual NuGet packages referenced in Kiriha.csproj /
    /// Directory.Packages.props; if a dependency is added or removed, update
    /// this list in the same change.
    /// </summary>
    public IReadOnlyList<CreditEntry> Libraries { get; } = new[]
    {
        new CreditEntry("C#",                             "The main programming language",             "https://learn.microsoft.com/dotnet/csharp/"),
        new CreditEntry(".NET 10",                        "MIT · Application runtime",                 "https://dotnet.microsoft.com/"),
        new CreditEntry("Avalonia UI",                    "MIT · Cross-platform XAML framework",       "https://avaloniaui.net"),
        new CreditEntry("CommunityToolkit.Mvvm",          "MIT · MVVM source generators",              "https://github.com/CommunityToolkit/dotnet"),
        new CreditEntry("Material.Icons.Avalonia",        "MIT · SKProCH",                             "https://github.com/SKProCH/Material.Icons"),
        new CreditEntry("AsyncImageLoader.Avalonia",      "MIT · Async image source",                  "https://github.com/AvaloniaUtils/AsyncImageLoader.Avalonia"),
        new CreditEntry("Entity Framework Core (SQLite)", "MIT · Microsoft",                           "https://learn.microsoft.com/ef/core/"),
        new CreditEntry("Serilog",                        "Apache 2.0 · Structured logging",           "https://serilog.net"),
        new CreditEntry("Velopack",                       "MIT · Auto-update framework",               "https://velopack.io"),
        new CreditEntry("DiscordRichPresence",            "MIT · Lachee",                              "https://github.com/Lachee/discord-rpc-csharp"),
        new CreditEntry("AnitomySharp",                   "MPL 2.0 · Filename parsing",                "https://github.com/erengy/anitomy"),
        new CreditEntry("Anisthesia",                     "MPL 2.0 · Local media tracking",            "https://github.com/erengy/anisthesia"),
        new CreditEntry("Microsoft.Toolkit.Uwp.Notifications", "MIT · Windows toast notifications",     "https://github.com/CommunityToolkit/WindowsCommunityToolkit"),
    };

    public AboutWindow()
    {
        DataContext = this;
        InitializeComponent();
        ApplyMica();
        VersionLabel.Text = $"v{AppInfo.Version}".ToUpperInvariant();
        Opened += OnOpened;
    }

    public void ApplyMica()
    {
        var settings = App.Services.GetRequiredService<Kiriha.Services.Data.SettingsService>().Current;
        if (settings.UI.EnableMica)
        {
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.Mica, Avalonia.Controls.WindowTransparencyLevel.AcrylicBlur };
            Background = null;
        }
        else
        {
            TransparencyLevelHint = new[] { Avalonia.Controls.WindowTransparencyLevel.None };
            ClearValue(BackgroundProperty);
        }
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        // High-DPI safety + owner-aware centring.
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var workArea = screen.WorkingArea;

        // 1. Shrink to fit the work area (40 px margin on every side).
        const int marginPx = 40;
        var maxWDip = System.Math.Max(200, (workArea.Width  - marginPx * 2) / scale);
        var maxHDip = System.Math.Max(150, (workArea.Height - marginPx * 2) / scale);
        if (Width  > maxWDip) Width  = maxWDip;
        if (Height > maxHDip) Height = maxHDip;

        // 2. Compute the actual window footprint in physical pixels.
        var winWPx = (int)System.Math.Ceiling(Width  * scale);
        var winHPx = (int)System.Math.Ceiling(Height * scale);

        // 3. Centre on owner when we can
        int x, y;
        if (Owner is Window owner && owner.IsVisible)
        {
            var ownerWPx = (int)(owner.Bounds.Width  * scale);
            var ownerHPx = (int)(owner.Bounds.Height * scale);
            x = owner.Position.X + (ownerWPx - winWPx) / 2;
            y = owner.Position.Y + (ownerHPx - winHPx) / 2;
        }
        else
        {
            x = workArea.X + (workArea.Width  - winWPx) / 2;
            y = workArea.Y + (workArea.Height - winHPx) / 2;
        }

        // 4. Clamp inside the work area
        x = System.Math.Clamp(x, workArea.X, workArea.X + System.Math.Max(0, workArea.Width  - winWPx));
        y = System.Math.Clamp(y, workArea.Y, workArea.Y + System.Math.Max(0, workArea.Height - winHPx));

        Position = new Avalonia.PixelPoint(x, y);
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;
        var p = e.GetCurrentPoint(this);
        if (!p.Properties.IsLeftButtonPressed) return;
        if (p.Position.Y > 34) return;
        BeginMoveDrag(e);
        e.Handled = true;
    }

    /// <summary>
    /// Generic row click — the URL is passed via the Button.Tag binding, so
    /// the same handler serves both Data Sources and Libraries lists. Rows
    /// without a URL get an empty Tag and are no-ops (the OpenInNew icon is
    /// hidden for them via HasUrl binding).
    /// </summary>
    private void OnEntryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrEmpty(url))
            UIUtils.OpenUrl(url);
    }
}
