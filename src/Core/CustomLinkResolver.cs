using System;
using Kiriha.Models;

namespace Kiriha.Core;

/// <summary>
/// Substitutes anime fields into a user-defined URL template. All string
/// placeholders are URL-encoded so the template can be naive about escaping
/// (e.g. <c>https://google.com/search?q={title}</c> just works for titles
/// containing spaces, ampersands, etc.).
///
/// Numeric placeholders (ids) are passed through verbatim — they're already
/// URL-safe and we don't want them quoted.
///
/// Supported placeholders:
///   {title}     — original (English / romaji) title
///   {english}   — explicit English title (falls back to {title})
///   {russian}   — Russian title (falls back to {title})
///   {japanese}  — Japanese title (may be empty)
///   {id}        — MAL ID
///   {malId}     — alias of {id}
///   {shikiId}   — Shikimori ID. Currently identical to MAL ID because Kiriha
///                 stores a single canonical id; kept as a separate placeholder
///                 so templates can document intent and we can divorce the two
///                 later without breaking user templates.
/// </summary>
public static class CustomLinkResolver
{
    public static string Resolve(string template, AnimeItem? anime)
    {
        if (string.IsNullOrEmpty(template) || anime == null) return template ?? string.Empty;

        var title = anime.Title ?? string.Empty;
        var english = !string.IsNullOrEmpty(anime.EnglishTitle) ? anime.EnglishTitle! : title;
        var russian = !string.IsNullOrEmpty(anime.RussianTitle) ? anime.RussianTitle! : title;
        var japanese = anime.JapaneseTitle ?? string.Empty;
        var id = anime.Id.ToString();

        return template
            .Replace("{title}", Uri.EscapeDataString(title))
            .Replace("{english}", Uri.EscapeDataString(english))
            .Replace("{russian}", Uri.EscapeDataString(russian))
            .Replace("{japanese}", Uri.EscapeDataString(japanese))
            .Replace("{malId}", id)
            .Replace("{shikiId}", id)
            .Replace("{id}", id);
    }
}
