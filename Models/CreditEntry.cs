namespace Kiriha.Models;

/// <summary>
/// Single line on the About dialog's credits list. Lives at top level (not
/// nested inside <c>AboutWindow</c>) so XAML compiled bindings can resolve
/// <c>x:DataType="m:CreditEntry"</c> without referencing a nested-type token
/// the XAML parser can't spell.
/// </summary>
public sealed record CreditEntry(string Name, string Note, string? Url = null)
{
    public bool HasUrl => !string.IsNullOrEmpty(Url);
}
