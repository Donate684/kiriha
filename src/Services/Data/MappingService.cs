using Kiriha.Utils;
using Kiriha.Utils.Parsing;
using Kiriha.Utils.Collections;
using Kiriha.Utils.Async;
using Kiriha.Utils.Graphs;
using Kiriha.Utils.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kiriha.Models;
using Kiriha.Services.Api;
using Serilog;

namespace Kiriha.Services.Data;

public partial class MappingService
{
    private readonly MalApiService _malApi;
    private readonly ManualMappingService _manualMapping;
    private readonly Repositories.IMalSearchCacheRepository _malSearchCache;
    private readonly RecognitionCache _recognitionCache;
    private readonly ConcurrentDictionary<string, int> _sessionCache = new();

    public MappingService(MalApiService malApi, ManualMappingService manualMapping, Repositories.IMalSearchCacheRepository malSearchCache, RecognitionCache recognitionCache)
    {
        _malApi = malApi;
        _manualMapping = manualMapping;
        _malSearchCache = malSearchCache;
        _recognitionCache = recognitionCache;
    }

    public void ClearRecognitionCaches()
    {
        _sessionCache.Clear();
        _recognitionCache.Clear();
    }

    public void AddMapping(string title, int malId)
    {
        _manualMapping.AddMapping(title, malId);
        ClearRecognitionCaches();
    }

    public void RemoveMapping(string title)
    {
        _manualMapping.RemoveMapping(title);
        // Clear session cache to force a re-evaluation
        ClearRecognitionCaches();
    }

    public bool IsManuallyMapped(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        string normOriginal = Normalize(title);
        if (_manualMapping.TryGetMapping(normOriginal, out _)) return true;
        
        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var titleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
        string cleanTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(title);
        
        string normClean = Normalize(cleanTitle);
        if (_manualMapping.TryGetMapping(normClean, out _)) return true;
        
        return false;
    }

    public bool IsNegativelyMapped(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        if (_manualMapping.IsNegativelyMapped(Normalize(title))) return true;

        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var titleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
        string cleanTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(title);
        if (_manualMapping.IsNegativelyMapped(Normalize(cleanTitle))) return true;

        return false;
    }

    public void AddNegativeMapping(string title)
    {
        _manualMapping.AddNegativeMapping(title);
        ClearRecognitionCaches();
    }

    public async Task<int?> GetIdFromTitleAsync(string title, IEnumerable<AnimeItem> userList)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var (cleanTitle, searchTitle, parsedSeason, parsedEpisode) = ParseAnimeTitle(title);
        
        string normalized = cleanTitle.Trim().ToLowerInvariant();
        string normalizedWithSeason = searchTitle.Trim().ToLowerInvariant();

        // 0. Session Cache (0 = cached negative result)
        if (_sessionCache.TryGetValue(normalizedWithSeason, out int id)) return id == 0 ? null : id;
        if (normalizedWithSeason != normalized && _sessionCache.TryGetValue(normalized, out id)) return id == 0 ? null : id;

        // 1. Manual Mappings
        string normOriginal = Normalize(title);
        string normClean = Normalize(cleanTitle);
        
        if (_manualMapping.TryGetMapping(normOriginal, out id)) return id;
        if (_manualMapping.TryGetMapping(normClean, out id)) return id;
        if (_manualMapping.TryGetMapping(normalizedWithSeason, out id)) return id;
        if (normalizedWithSeason != normalized && _manualMapping.TryGetMapping(normalized, out id)) return id;

        // 2. Recognition Cache
        string normSearch = Normalize(searchTitle);

        var cachedMatches = _recognitionCache.Lookup(normSearch);
        if (cachedMatches != null)
        {
            var matches = cachedMatches.OrderByDescending(m => m.Weight).ToList();
            foreach (var match in matches)
            {
                if (match.Id == 0) continue;
                var anime = userList.FirstOrDefault(x => x.Id == match.Id);
                if (anime != null && !IsValidMatch(anime, parsedEpisode)) continue;
                
                _sessionCache[normalizedWithSeason] = match.Id;
                return match.Id;
            }
        }

        // 3. User List Exact Match
        var localMatch = userList.FirstOrDefault(x => 
            string.Equals(x.Title, searchTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.EnglishTitle, searchTitle, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.RussianTitle, searchTitle, StringComparison.OrdinalIgnoreCase));

        // Don't fall back to the bare title when a higher season was explicitly
        // parsed from the filename ("2nd Season", "S02", etc.) — otherwise we'd
        // happily match e.g. "Sousou no Frieren 2nd Season - 01" to the S1 entry
        // in the user list. Let SearchOnMalAsync handle these cases instead.
        if (localMatch == null && searchTitle != cleanTitle && parsedSeason <= 1)
        {
            localMatch = userList.FirstOrDefault(x => 
                string.Equals(x.Title, cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.EnglishTitle, cleanTitle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.RussianTitle, cleanTitle, StringComparison.OrdinalIgnoreCase));
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode))
        {
            _sessionCache[normalizedWithSeason] = localMatch.Id;
            return localMatch.Id;
        }

        // 4. User List Normalized Match
        string normTitle = Normalize(cleanTitle);
        string normSearchTitle = Normalize(searchTitle);
        
        localMatch = userList.FirstOrDefault(x => 
            Normalize(x.Title) == normSearchTitle || 
            Normalize(x.EnglishTitle ?? "") == normSearchTitle ||
            Normalize(x.RussianTitle ?? "") == normSearchTitle);
            
        // Same season-aware guard as in step 3: never collapse a "Season 2+"
        // query down to the base title here, otherwise the normalized fallback
        // silently maps "Sousou no Frieren 2nd Season - 01" to the S1 entry.
        if (localMatch == null && normSearchTitle != normTitle && parsedSeason <= 1)
        {
            localMatch = userList.FirstOrDefault(x => 
                Normalize(x.Title) == normTitle || 
                Normalize(x.EnglishTitle ?? "") == normTitle ||
                Normalize(x.RussianTitle ?? "") == normTitle);
        }

        if (localMatch != null && IsValidMatch(localMatch, parsedEpisode))
        {
            _sessionCache[normalizedWithSeason] = localMatch.Id;
            return localMatch.Id;
        }

        return null;
    }

    private bool IsValidMatch(AnimeItem match, int? episodeNumber)
    {
        if (episodeNumber == null) return true;
        if (match.TotalEpisodes <= 1) return true;
        if (episodeNumber > match.TotalEpisodes) return false;
        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[sS](\d{1,2})[eE]\d+|\b[sS]eason\s*(\d{1,2})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SeasonRegex();

    private int ExtractSeason(string title, AnitomySharp.Element? seasonElement)
    {
        if (seasonElement != null && int.TryParse(seasonElement.Value, out int s)) return s;
        
        var match = SeasonRegex().Match(title);
        if (match.Success)
        {
            int.TryParse(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, out int season);
            return season;
        }
        return 0;
    }

    private (string CleanTitle, string SearchTitle, int ParsedSeason, int? ParsedEpisode) ParseAnimeTitle(string title)
    {
        var parsed = Kiriha.Utils.Parsing.AnimeParseCache.Parse(title);
        var titleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle);
        var seasonElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeSeason);
        var typeElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementAnimeType);
        var subTitleElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeTitle);
        var otherElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementOther);
        var episodeElement = parsed.FirstOrDefault(x => x.Category == AnitomySharp.Element.ElementCategory.ElementEpisodeNumber);

        int parsedSeason = ExtractSeason(title, seasonElement);
        int? parsedEpisode = null;
        if (episodeElement != null && int.TryParse(episodeElement.Value, out int ep))
        {
            parsedEpisode = ep;
        }

        string cleanTitle = titleElement != null ? titleElement.Value : Path.GetFileNameWithoutExtension(title);
        
        if (episodeElement == null)
        {
            if (subTitleElement != null && !cleanTitle.Contains(subTitleElement.Value, StringComparison.OrdinalIgnoreCase))
                cleanTitle = $"{cleanTitle} {subTitleElement.Value}";
            if (otherElement != null && !cleanTitle.Contains(otherElement.Value, StringComparison.OrdinalIgnoreCase))
                cleanTitle = $"{cleanTitle} {otherElement.Value}";
        }

        string searchTitle = cleanTitle;
        if (typeElement != null && !string.IsNullOrEmpty(typeElement.Value))
        {
            string type = typeElement.Value.ToUpperInvariant();
            if (type == "OVA" || type == "OAD" || type == "SPECIAL" || type == "SP" || type == "ONA")
                searchTitle = $"{cleanTitle} {type}";
        }

        if (searchTitle == cleanTitle && parsedSeason > 1)
            searchTitle = $"{cleanTitle} Season {parsedSeason}";

        return (cleanTitle, searchTitle, parsedSeason, parsedEpisode);
    }

    public async Task<int?> SearchOnMalAsync(string title)
    {
        var (cleanTitle, searchQuery, _, _) = ParseAnimeTitle(title);

        string normQuery = Normalize(searchQuery);
        if (_sessionCache.TryGetValue(normQuery, out int cachedId))
            return cachedId == 0 ? null : cachedId;

        var cachedMatches = _recognitionCache.Lookup(normQuery);
        if (cachedMatches != null)
        {
            var bestMatch = cachedMatches.OrderByDescending(m => m.Weight).FirstOrDefault();
            return bestMatch.Id != 0 ? bestMatch.Id : null;
        }

        // Persistent L2: DB-backed cache. Survives restarts so re-scanning the
        // same library doesn't re-hit MAL for queries we've resolved before.
        // GetMalSearchCacheAsync already enforces TTL (positive 30d, negative 7d)
        // and returns null on expired entries.
        try
        {
            var dbHit = await _malSearchCache.GetAsync(normQuery);
            if (dbHit != null)
            {
                _sessionCache[normQuery] = dbHit.AnimeId; // promote to L1
                return dbHit.AnimeId == 0 ? null : dbHit.AnimeId;
            }
        }
        catch (Exception ex)
        {
            // Cache miss on error — fall through to live API. Don't let a
            // transient DB hiccup break title resolution.
            Log.Debug(ex, "MappingService: MAL search cache lookup failed for {Query}", normQuery);
        }

        var searchResults = await _malApi.SearchAnimeAsync(searchQuery);
        if (!searchResults.Any() && searchQuery != cleanTitle)
            searchResults = await _malApi.SearchAnimeAsync(cleanTitle);

        if (!searchResults.Any())
        {
            // Negative cache: avoid re-hitting MAL for the same unresolvable title.
            _sessionCache[normQuery] = 0;
            try { await _malSearchCache.UpsertAsync(normQuery, 0, 0f); }
            catch (Exception ex) { Log.Debug(ex, "MappingService: failed to persist negative MAL search cache"); }
            return null;
        }

        string normQ = Normalize(searchQuery);
        var queryWords = normQ.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var bestMalMatch = searchResults.Take(5)
            .Select(r => {
                float score = 0;
                
                var titles = new List<string> { r.Title };
                if (!string.IsNullOrEmpty(r.EnglishTitle)) titles.Add(r.EnglishTitle);
                if (!string.IsNullOrEmpty(r.JapaneseTitle)) titles.Add(r.JapaneseTitle);
                if (r.AlternativeTitles != null) titles.AddRange(r.AlternativeTitles);
                
                foreach(var t in titles) {
                    string normT = Normalize(t);
                    var titleWords = normT.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    
                    int matchingWords = queryWords.Count(qw => titleWords.Contains(qw));
                    float currentScore = queryWords.Length > 0 ? (matchingWords / (float)queryWords.Length) * 70 : 0;
                    
                    if (normT == normQ) currentScore = 100;

                    string[] criticalKeywords = { "movie", "ova", "oad", "special", "ii", "2", "iii", "3", "iv", "4", "v", "5" };
                    foreach (var word in criticalKeywords)
                    {
                        bool inQuery = queryWords.Contains(word, StringComparer.OrdinalIgnoreCase);
                        bool inTitle = titleWords.Contains(word, StringComparer.OrdinalIgnoreCase);
                        
                        if (inQuery && inTitle) currentScore += 30;
                        else if (inQuery && !inTitle) currentScore -= 15;
                    }
                    
                    if (currentScore > score) score = currentScore;
                }
                
                return new { Result = r, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => searchResults.IndexOf(x.Result))
            .First();

        var resolvedId = bestMalMatch.Result.Id;
        _sessionCache[normQuery] = resolvedId;
        _recognitionCache.AddMatch(normQuery, resolvedId, bestMalMatch.Score);

        // Persist to DB so future sessions skip the MAL round-trip.
        try { await _malSearchCache.UpsertAsync(normQuery, resolvedId, bestMalMatch.Score); }
        catch (Exception ex) { Log.Debug(ex, "MappingService: failed to persist positive MAL search cache"); }

        return resolvedId;
    }

    private string Normalize(string s) => Kiriha.Utils.Parsing.AnimeStringHelper.Normalize(s);
}
