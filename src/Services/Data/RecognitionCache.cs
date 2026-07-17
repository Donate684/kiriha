using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Kiriha.Models;
using Kiriha.Utils.Parsing;

namespace Kiriha.Services.Data;

public record struct WeightedMatch(int Id, float Weight);

public class RecognitionCache
{
    private readonly ConcurrentDictionary<string, List<WeightedMatch>> _cache = new();

    public void BuildIndex(IEnumerable<AnimeItem> collection)
    {
        _cache.Clear();
        foreach (var anime in collection)
        {
            // Index Title, EnglishTitle, RussianTitle with weight 1.0
            IndexTitle(anime.Title, anime.Id, 1.0f);
            IndexTitle(anime.EnglishTitle, anime.Id, 1.0f);
            IndexTitle(anime.RussianTitle, anime.Id, 1.0f);

            // Index Synonyms with weight 0.5
            if (anime.AlternativeTitles != null)
            {
                foreach (var alt in anime.AlternativeTitles)
                {
                    IndexTitle(alt, anime.Id, 0.5f);
                }
            }

            // Index Title (Year) with weight 0.5
            if (anime.StartYear.HasValue)
            {
                IndexTitle($"{anime.Title} ({anime.StartYear})", anime.Id, 0.5f);
                if (!string.IsNullOrEmpty(anime.EnglishTitle))
                    IndexTitle($"{anime.EnglishTitle} ({anime.StartYear})", anime.Id, 0.5f);
                if (!string.IsNullOrEmpty(anime.RussianTitle))
                    IndexTitle($"{anime.RussianTitle} ({anime.StartYear})", anime.Id, 0.5f);
            }
        }
    }

    private void IndexTitle(string? title, int id, float weight)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        string norm = AnimeStringHelper.Normalize(title);
        if (string.IsNullOrWhiteSpace(norm)) return;

        _cache.AddOrUpdate(norm,
            _ => new List<WeightedMatch> { new(id, weight) },
            (_, list) =>
            {
                // avoid duplicate id in same normalized title, keep max weight
                var existingIndex = list.FindIndex(x => x.Id == id);
                if (existingIndex >= 0)
                {
                    if (list[existingIndex].Weight < weight)
                    {
                        list[existingIndex] = new WeightedMatch(id, weight);
                    }
                }
                else
                {
                    list.Add(new WeightedMatch(id, weight));
                }
                return list;
            });
    }

    public List<WeightedMatch>? Lookup(string normalizedTitle)
    {
        if (_cache.TryGetValue(normalizedTitle, out var matches))
            return matches;
        return null;
    }
    
    public void Clear() => _cache.Clear();
    
    public void AddMatch(string normalizedTitle, int id, float weight)
    {
        _cache.AddOrUpdate(normalizedTitle,
            _ => new List<WeightedMatch> { new(id, weight) },
            (_, list) =>
            {
                if (!list.Any(x => x.Id == id))
                    list.Add(new WeightedMatch(id, weight));
                return list;
            });
    }
}
