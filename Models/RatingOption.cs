using System;
using System.Globalization;
using Kiriha.Core;

namespace Kiriha.Models;

public record RatingOption(string Value, string Description, int StarCount)
{
    public string Display => Description != "" ? $"{Value} {Description}" : Value;
    public override string ToString() => Display;
}

public static class RatingHelper
{
    public static int GetStarCount(string? scoreStr, string context = "MyList")
    {
        if (string.IsNullOrEmpty(scoreStr) || scoreStr == "-") return 0;
        if (scoreStr == "10" || scoreStr == "5+") return 5;

        if (double.TryParse(scoreStr.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
        {
            if (context == "Community" || context == "Seasons")
            {
                return score switch
                {
                    >= 8.2 => 5,
                    >= 7.5 => 4,
                    >= 6.8 => 3,
                    >= 6.0 => 2,
                    >= 0.1 => 1,
                    _ => 0
                };
            }
            else // MyList
            {
                return score switch
                {
                    >= 9.0 => 5,
                    >= 7.0 => 4,
                    >= 5.0 => 3,
                    >= 3.0 => 2,
                    >= 0.1 => 1,
                    _ => 0
                };
            }
        }
        return 0;
    }

    public static bool IsElite(string? scoreStr, string context = "MyList")
    {
        if (string.IsNullOrEmpty(scoreStr) || scoreStr == "-") return false;
        if (scoreStr == "10" || scoreStr == "5+") return true;

        if (double.TryParse(scoreStr.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
        {
            if (context == "Community" || context == "Seasons")
                return score >= 8.75;
            
            return score >= 10.0;
        }
        return false;
    }

    public static RatingOption GetRatingOption(string? scoreStr)
    {
        if (string.IsNullOrEmpty(scoreStr) || scoreStr == "-") 
            return new RatingOption("-", "", 0);

        if (double.TryParse(scoreStr.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
        {
            if (score <= 0) return new RatingOption("-", "", 0);

            return score switch
            {
                >= 10 => new RatingOption("10", $"({UIUtils.GetLoc("filters.score.masterpiece")})", 5),
                >= 9 => new RatingOption("9", $"({UIUtils.GetLoc("filters.score.excellent")})", 5),
                >= 8 => new RatingOption("8", $"({UIUtils.GetLoc("filters.score.very_good")})", 4),
                >= 7 => new RatingOption("7", $"({UIUtils.GetLoc("filters.score.good")})", 4),
                >= 6 => new RatingOption("6", $"({UIUtils.GetLoc("filters.score.average")})", 3),
                >= 5 => new RatingOption("5", $"({UIUtils.GetLoc("filters.score.mediocre")})", 3),
                >= 4 => new RatingOption("4", $"({UIUtils.GetLoc("filters.score.bad")})", 2),
                >= 3 => new RatingOption("3", $"({UIUtils.GetLoc("filters.score.very_bad")})", 2),
                >= 2 => new RatingOption("2", $"({UIUtils.GetLoc("filters.score.horrible")})", 1),
                >= 1 => new RatingOption("1", $"({UIUtils.GetLoc("filters.score.appalling")})", 1),
                _ => new RatingOption("-", "", 0)
            };
        }

        return new RatingOption("-", "", 0);
    }
}
