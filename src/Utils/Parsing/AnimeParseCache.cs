using System;
using System.Collections.Generic;
using System.Linq;
using AnitomySharp;
using Kiriha.Utils.Collections;

namespace Kiriha.Utils.Parsing;

/// <summary>
/// Memoizing wrapper around <see cref="AnitomySharp.AnitomySharp.Parse(string)"/>.
///
/// The same filename / window title is parsed many times per session:
///   * <see cref="Kiriha.Services.Data.MappingService"/> calls Parse 4 times for
///     a single matching attempt (IsManuallyMapped, IsNegativelyMapped,
///     GetIdFromTitleAsync, SearchOnMalAsync).
///   * Anisthesia detection strategies (WindowTitle / HandleEnumeration) parse
///     the same title every detection tick.
/// A single parse is several milliseconds; multiplied by N files × M ticks it
/// shows up clearly in CPU profiles.
///
/// The returned list MUST be treated as read-only — it is shared across all
/// callers. Every existing call site only enumerates / uses FirstOrDefault, so
/// this is safe today; the IReadOnlyList signature enforces it going forward.
/// </summary>
public static class AnimeParseCache
{
    // 2048 entries × ~32 elements × ~80 bytes ≈ ~5 MB worst case, but in
    // practice element values are short strings and entries are tiny.
    private static readonly LruStringMemoizer<IReadOnlyList<Element>> _cache = new(2048);

    private static readonly IReadOnlyList<Element> Empty = Array.Empty<Element>();

    public static IReadOnlyList<Element> Parse(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return Empty;
        return _cache.GetOrAdd(filename, static key =>
            (IReadOnlyList<Element>)AnitomySharp.AnitomySharp.Parse(key).ToList());
    }
}
