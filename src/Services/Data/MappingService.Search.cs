using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace Kiriha.Services.Data;

public partial class MappingService
{
    public virtual async Task<int?> SearchOnMalAsync(string title)
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
            .Select(r =>
            {
                float score = 0;

                var titles = new List<string> { r.Title };
                if (!string.IsNullOrEmpty(r.EnglishTitle)) titles.Add(r.EnglishTitle);
                if (!string.IsNullOrEmpty(r.JapaneseTitle)) titles.Add(r.JapaneseTitle);
                if (r.AlternativeTitles != null) titles.AddRange(r.AlternativeTitles);

                foreach (var t in titles)
                {
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
}
