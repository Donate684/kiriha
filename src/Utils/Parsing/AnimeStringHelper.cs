using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kiriha.Utils.Parsing;

public static class AnimeStringHelper
{
    private static readonly Dictionary<string, string> RomanNumerals = new()
    {
        { "i", "1" }, { "ii", "2" }, { "iii", "3" }, { "iv", "4" }, { "v", "5" },
        { "vi", "6" }, { "vii", "7" }, { "viii", "8" }, { "ix", "9" }, { "x", "10" },
        { "xi", "11" }, { "xii", "12" }, { "xiii", "13" }, { "xiv", "14" }, { "xv", "15" }
    };

    private static readonly Dictionary<string, string> Ordinals = new()
    {
        { "first", "1st" }, { "second", "2nd" }, { "third", "3rd" },
        { "fourth", "4th" }, { "fifth", "5th" }, { "sixth", "6th" },
        { "seventh", "7th" }, { "eighth", "8th" }, { "ninth", "9th" }
    };

    private static readonly Dictionary<string, string> SeasonsMap = new()
    {
        { "1st season", "1" }, { "season 1", "1" }, { "series 1", "1" }, { "s1", "1" },
        { "2nd season", "2" }, { "season 2", "2" }, { "series 2", "2" }, { "s2", "2" },
        { "3rd season", "3" }, { "season 3", "3" }, { "series 3", "3" }, { "s3", "3" },
        { "4th season", "4" }, { "season 4", "4" }, { "series 4", "4" }, { "s4", "4" },
        { "5th season", "5" }, { "season 5", "5" }, { "series 5", "5" }, { "s5", "5" },
        { "6th season", "6" }, { "season 6", "6" }, { "series 6", "6" }, { "s6", "6" }
    };

    private static readonly Dictionary<string, string> GenericReplacements = new()
    {
        { "&", "and" },
        { "the animation", "" },
        { "the", "" },
        { "episode", "" },
        { "oad", "ova" },
        { "oav", "ova" },
        { "specials", "sp" },
        { "special", "sp" },
        { "(tv)", "" }
    };

    // Pre-compiled Regexes for maximum performance
    private static readonly Regex SpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex RomanRegex;
    private static readonly Regex OrdinalRegex;
    private static readonly Regex SeasonRegex;
    private static readonly Regex GenericRegex;

    static AnimeStringHelper()
    {
        RomanRegex = BuildGroupRegex(RomanNumerals.Keys);
        OrdinalRegex = BuildGroupRegex(Ordinals.Keys);
        SeasonRegex = BuildGroupRegex(SeasonsMap.Keys);
        GenericRegex = BuildGroupRegex(GenericReplacements.Keys);
    }

    private static Regex BuildGroupRegex(IEnumerable<string> patterns)
    {
        // Sort by length descending to match longer phrases first (e.g., "1st season" before "1")
        var escaped = patterns.OrderByDescending(p => p.Length).Select(Regex.Escape);
        string pattern = $@"(?<=^|[^a-z0-9])({string.Join("|", escaped)})(?=[^a-z0-9]|$)";
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // Hot-path memoization. MappingService normalizes the same title (and each
    // of x.Title / x.EnglishTitle / x.RussianTitle) repeatedly during one
    // matching attempt; Anisthesia strategies normalize the same window title
    // every detection tick. Capped at 2048 entries — way more than the unique
    // title set of any realistic session.
    private static readonly LruStringMemoizer<string> _normalizeCache = new(2048);

    public static string Normalize(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        return _normalizeCache.GetOrAdd(title, static key => NormalizeCore(key));
    }

    private static string NormalizeCore(string title)
    {
        // 1. Initial cleanup and lower case
        string result = title.Trim().ToLowerInvariant();

        // 2. Specific character replacements
        result = result.Replace("ō", "ou").Replace("ū", "uu")
                       .Replace("@", "a").Replace("Ч", "x");

        // 3. Batch replacements using compiled regexes
        result = ApplyGroupReplacements(result, RomanRegex, RomanNumerals);
        result = ApplyGroupReplacements(result, OrdinalRegex, Ordinals);
        result = ApplyGroupReplacements(result, SeasonRegex, SeasonsMap);
        result = ApplyGroupReplacements(result, GenericRegex, GenericReplacements);

        // 4. Punctuation removal and simplification using StringBuilder
        var sb = new StringBuilder(result.Length);
        foreach (char c in result)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
            else
                sb.Append(' ');
        }
        
        return SpacesRegex.Replace(sb.ToString(), " ").Trim();
    }

    private static string ApplyGroupReplacements(string input, Regex regex, Dictionary<string, string> map)
    {
        return regex.Replace(input, match => 
            map.TryGetValue(match.Value.ToLowerInvariant(), out var replacement) ? replacement : match.Value);
    }

    private static readonly Regex ShikiTagsRegex = new(@"\[\w+(=[^\]]+)?\](.*?)\[/\w+\]", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex GenericTagsRegex = new(@"\[/?\w+(=[^\]]+)?\]", RegexOptions.Compiled);

    public static string CleanShikiDescription(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        
        // Handle Shikimori's placeholder for blocked titles
        if (input.Contains("Заблокировано по требованию Роскомнадзора")) return string.Empty;

        // 1. First replace [tag=val]Content[/tag] with Content
        string result = ShikiTagsRegex.Replace(input, "$2");

        // 2. Then remove any stray tags like [i] or [b] that might be left
        result = GenericTagsRegex.Replace(result, "");

        return result.Trim();
    }
}
