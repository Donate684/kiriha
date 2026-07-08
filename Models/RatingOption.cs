using System.Globalization;
using Kiriha.Core;
using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;

namespace Kiriha.Models;

public record RatingOption(string Value, string Description)
{
    public string Display => Description != "" ? $"{Value} {Description}" : Value;
    public override string ToString() => Display;
}

public static class RatingHelper
{
    public static RatingOption GetRatingOption(string? scoreStr)
    {
        if (string.IsNullOrEmpty(scoreStr) || scoreStr == "-") 
            return new RatingOption("-", "");

        if (double.TryParse(scoreStr.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double score))
        {
            if (score <= 0) return new RatingOption("-", "");

            return score switch
            {
                >= 10 => new RatingOption("10", $"({UIUtils.GetLoc("filters.score.masterpiece")})"),
                >= 9 => new RatingOption("9", $"({UIUtils.GetLoc("filters.score.excellent")})"),
                >= 8 => new RatingOption("8", $"({UIUtils.GetLoc("filters.score.very_good")})"),
                >= 7 => new RatingOption("7", $"({UIUtils.GetLoc("filters.score.good")})"),
                >= 6 => new RatingOption("6", $"({UIUtils.GetLoc("filters.score.average")})"),
                >= 5 => new RatingOption("5", $"({UIUtils.GetLoc("filters.score.mediocre")})"),
                >= 4 => new RatingOption("4", $"({UIUtils.GetLoc("filters.score.bad")})"),
                >= 3 => new RatingOption("3", $"({UIUtils.GetLoc("filters.score.very_bad")})"),
                >= 2 => new RatingOption("2", $"({UIUtils.GetLoc("filters.score.horrible")})"),
                >= 1 => new RatingOption("1", $"({UIUtils.GetLoc("filters.score.appalling")})"),
                _ => new RatingOption("-", "")
            };
        }

        return new RatingOption("-", "");
    }
}
