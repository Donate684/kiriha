using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Kiriha.Core;
using Kiriha.Models;

namespace Kiriha.Services.Tracking.Anisthesia.Strategies;

public class WindowTitleStrategy
{
    public static ParsedMedia? Apply(AnisthesiaPlayer player, uint pid, IntPtr hWnd)
    {
        string windowTitle = Win32Api.GetWindowTitle(hWnd);
        if (string.IsNullOrEmpty(windowTitle)) return null;

        if (string.IsNullOrEmpty(player.WindowTitleFormat))
        {
            // If no format specified, but window is detected, return title as is
            return new ParsedMedia { OriginalTitle = windowTitle, AnimeTitle = windowTitle, IsPlaying = true };
        }

        try
        {
            var match = Regex.Match(windowTitle, player.WindowTitleFormat);
            if (match.Success)
            {
                bool groupMatched = false;
                // Find first non-empty group (skipping the full match)
                for (int i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success && !string.IsNullOrEmpty(match.Groups[i].Value))
                    {
                        string extracted = match.Groups[i].Value;
                        
                        // Further parse with Anitomy to get episode
                        var elements = Kiriha.Utils.AnimeParseCache.Parse(extracted);
                        var titleElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
                        var subTitleElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeTitle);
                        var otherElement = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementOther);
                        var episode = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber)?.Value;
                        var season = elements.FirstOrDefault(e => e.Category == AnitomySharp.Element.ElementCategory.ElementAnimeSeason)?.Value;

                        string animeTitle = titleElement != null ? titleElement.Value : extracted;
                        string episodeTitle = string.Empty;

                        if (subTitleElement != null)
                            episodeTitle = subTitleElement.Value;

                        if (otherElement != null)
                            episodeTitle = string.IsNullOrEmpty(episodeTitle) ? otherElement.Value : $"{episodeTitle} {otherElement.Value}";

                        return new ParsedMedia { 
                            OriginalTitle = extracted, 
                            AnimeTitle = animeTitle, 
                            EpisodeTitle = episodeTitle,
                            Episode = episode ?? string.Empty,
                            Season = season ?? string.Empty,
                            IsPlaying = true 
                        };
                    }
                }
                
                // If there were capturing groups defined in regex, but none matched, 
                // it means the "idle" part of the regex matched (e.g. "^MPC-BE.*")
                // In this case we return null because nothing is actually playing.
                if (player.WindowTitleFormat.Contains("(") && !groupMatched)
                {
                    return null;
                }

                // If no capturing groups in regex at all, we take the whole match
                return new ParsedMedia { OriginalTitle = windowTitle, AnimeTitle = windowTitle, IsPlaying = true };
            }
        }
        catch
        {
            // Invalid regex or other error
        }

        return null;
    }
}
