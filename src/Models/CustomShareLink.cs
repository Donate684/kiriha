using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Kiriha.Models;

/// <summary>
/// User-defined share button shown next to MAL/Shiki on the anime details and
/// now-playing cards. <see cref="UrlTemplate"/> supports the placeholders
/// resolved by <see cref="Kiriha.Core.CustomLinkResolver"/> (e.g. {title},
/// {russian}, {english}, {japanese}, {id}, {malId}, {shikiId}).
///
/// Inherits from ObservableObject so settings UI bindings (TextBox.Text, etc.)
/// notify the SettingsViewModel and trigger a save.
/// </summary>
public partial class CustomShareLink : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _urlTemplate = string.Empty;

    /// <summary>
    /// Material Icons name (e.g. "Web", "Magnify", "Television"). Used only
    /// when <see cref="IconPath"/> is empty. Defaults to "Web".
    /// </summary>
    [ObservableProperty]
    private string _iconKind = "Web";

    /// <summary>
    /// Optional path to a user-supplied icon file (PNG/JPG/SVG/etc.). When
    /// set, the UI shows this image instead of <see cref="IconKind"/>.
    /// Stored as an absolute path under <see cref="Kiriha.Core.PathHelper.GetCustomIconsPath"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIconPath))]
    [NotifyPropertyChangedFor(nameof(HasNoIconPath))]
    private string? _iconPath;

    /// <summary>True when the user has assigned a custom image. Drives UI visibility.</summary>
    public bool HasIconPath => !string.IsNullOrWhiteSpace(IconPath);

    /// <summary>Negation of <see cref="HasIconPath"/> — used as the MaterialIcon fallback gate.</summary>
    public bool HasNoIconPath => !HasIconPath;
}
