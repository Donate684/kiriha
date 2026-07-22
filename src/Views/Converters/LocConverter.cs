using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Kiriha.Core;

namespace Kiriha.Views.Converters;

public class LocConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum e)
        {
            value = e.ToString();
        }

        string? param = parameter?.ToString();
        string? keyStr = value?.ToString();

        // Special wizard logic
        if (param == "language_step") return keyStr == "language";
        if (param == "theme_step") return keyStr == "theme";
        if (param == "mal_step") return keyStr == "mal_login";
        if (param == "shiki_step") return keyStr == "shiki_login";
        if (param == "scrobbler_step") return keyStr == "scrobbler";
        if (param == "system_step") return keyStr == "system_settings";
        if (param == "advanced_step") return keyStr == "advanced_localization";
        if (param == "theme_icon")
        {
            if (value is ThemeType tt)
            {
                return tt switch
                {
                    ThemeType.Light => "Sun",
                    ThemeType.Dark => "Moon",
                    _ => "Monitor"
                };
            }
            return keyStr?.ToLowerInvariant() switch
            {
                "???????" or "light" => "Sun",
                "??????" or "??????" or "dark" => "Moon",
                _ => "Monitor"
            };
        }
        if (param == "language_check") return keyStr == "language";

        // Button logic based on IsLastStep (value is bool)
        if (param == "btn_text")
        {
            bool isLast = (bool)(value ?? false);
            return UIUtils.GetLoc(isLast ? "wizard.finish" : "wizard.next");
        }
        if (param == "show_arrow") return !(bool)(value ?? false);
        if (param == "eye_logic") return (bool)value! ? "EyeOffOutline" : "EyeOutline";

        // Style logic
        if (param == "radius_logic") return (bool)value! ? new CornerRadius(8, 0, 0, 0) : new CornerRadius(0);
        if (param == "thickness_logic") return (bool)value! ? new Thickness(1, 1, 0, 0) : new Thickness(0);
        if (param == "shadow_logic") return (bool)value! ? BoxShadows.Parse("-2 0 12 0 #08000000") : new BoxShadows();

        // Formatting logic: param="format:l.Key"
        if (param != null && param.StartsWith("format:"))
        {
            string formatKey = param.Substring(7);
            if (Application.Current != null && Application.Current.Resources.TryGetResource(formatKey, ThemeVariant.Default, out var formatObj) && formatObj is string formatStr)
            {
                try
                {
                    if (value is Kiriha.Models.AnimeItem ai)
                        return string.Format(formatStr, ai.EpisodesAired, ai.TotalEpisodes);
                    return string.Format(formatStr, value);
                }
                catch { return value?.ToString(); }
            }
            return value?.ToString();
        }

        if (value is System.Collections.IEnumerable list && !(value is string))
        {
            var results = new System.Collections.Generic.List<string>();
            foreach (var item in list)
            {
                var translated = Convert(item, typeof(string), parameter, culture);
                if (translated != null) results.Add(translated.ToString()!);
            }
            return string.Join(", ", results);
        }

        if (value is string s || (value is Enum && (s = value.ToString()!) != null))
        {
            // Handle prefix if parameter is provided
            string keyToUse = (param != null && param != "Adult") ? s : (param == "Adult" ? $"Adult{s}" : s);

            // Case 1: Try lowercase version first (matches ru.json keys)
            string lowerKey = keyToUse.ToLowerInvariant().Replace(" ", "");
            // PascalCase -> snake_case fallback: "OnHold" -> "on_hold", "PlanToWatch" -> "plan_to_watch"
            string snakeKey = PascalToSnake(keyToUse);
            if (Application.Current != null)
            {
                if (Application.Current.Resources.TryGetValue($"l.{lowerKey}", out var translatedLower))
                    return translatedLower;
                if (snakeKey != lowerKey &&
                    Application.Current.Resources.TryGetValue($"l.{snakeKey}", out var translatedSnake))
                    return translatedSnake;

                // Case 1b: Try common prefixes for nested keys
                string[] commonPrefixes = { "anime.types.", "anime.seasons.", "anime.status.", "common.actions.", "common.status.", "filters.sort.", "genres.", "torrents.sort." };
                foreach (var prefix in commonPrefixes)
                {
                    if (Application.Current.Resources.TryGetValue($"l.{prefix}{lowerKey}", out var translatedNested))
                        return translatedNested;
                    if (snakeKey != lowerKey &&
                        Application.Current.Resources.TryGetValue($"l.{prefix}{snakeKey}", out var translatedNestedSnake))
                        return translatedNestedSnake;
                }
            }

            // Case 2: Handle "Winter 2026" -> localized season name + year.
            var parts = s.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                string firstPartKey = parts[0].ToLowerInvariant();
                if (Application.Current != null)
                {
                    if (Application.Current.Resources.TryGetValue($"l.{firstPartKey}", out var firstTranslated))
                    {
                        return $"{firstTranslated} {parts[1]}";
                    }
                    if (Application.Current.Resources.TryGetValue($"l.anime.seasons.{firstPartKey}", out var firstTranslatedSeason))
                    {
                        return $"{firstTranslatedSeason} {parts[1]}";
                    }
                }
            }

            // Case 3: Simple exact match with "l." prefix
            string key = keyToUse.Replace(" ", "");
            if (Application.Current != null && Application.Current.Resources.TryGetValue($"l.{key}", out var translatedExact))
            {
                return translatedExact;
            }

            // Case 3: Just capitalize first letter if nothing found
            if (!string.IsNullOrEmpty(s))
            {
                return char.ToUpper(s[0]) + (s.Length > 1 ? s.Substring(1) : "");
            }

            return s;
        }

        if (value is Avalonia.Styling.ThemeVariant t)
        {
            string key = $"Theme{t.Key}";
            if (Application.Current != null && Application.Current.Resources.TryGetValue($"l.{key}", out var translated))
            {
                return translated;
            }
            return key;
        }

        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    private static string PascalToSnake(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new System.Text.StringBuilder(input.Length + 4);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c) || c == '_' || c == '-')
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                continue;
            }

            if (char.IsUpper(c) && i > 0 && sb.Length > 0 && sb[sb.Length - 1] != '_')
                sb.Append('_');

            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}


