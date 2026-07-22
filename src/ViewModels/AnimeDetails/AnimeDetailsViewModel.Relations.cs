using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Kiriha.Models;
using Kiriha.Models.Entities;
using Serilog;

namespace Kiriha.ViewModels.AnimeDetails;

public partial class AnimeDetailsViewModel
{
    private async Task FetchRelationImageAsync(RelationItemVm vm)
    {
        var type = vm.Relation.TargetType?.ToLowerInvariant() ?? "";
        bool isAnime = type == "anime" || type == "tv" || type == "movie" || type == "ova" || type == "ona" || type == "special";

        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == vm.Relation.TargetMalId && (isAnime ? x.MediaKind == MediaKind.Anime : x.MediaKind != MediaKind.Anime));
        if (existing != null && !string.IsNullOrEmpty(existing.MainPictureUrl))
        {
            vm.ImageUrl = existing.MainPictureUrl;
            if (!string.IsNullOrEmpty(existing.Type))
            {
                vm.DisplayTargetType = FormatMediaType(existing.Type);
            }
            return;
        }

        try
        {
            AnimeItem? details = null;
            if (isAnime)
            {
                details = await _malApiService.GetAnimeDetailsAsync(vm.Relation.TargetMalId);
            }
            else
            {
                details = await _malApiService.GetMangaDetailsAsync(vm.Relation.TargetMalId);
            }

            if (details != null)
            {
                if (!string.IsNullOrEmpty(details.MainPictureUrl))
                {
                    vm.ImageUrl = details.MainPictureUrl;
                }
                if (!string.IsNullOrEmpty(details.Type))
                {
                    vm.DisplayTargetType = FormatMediaType(details.Type);
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Failed to fetch image for relation {TargetMalId}", vm.Relation.TargetMalId);
        }
    }

    private static string FormatMediaType(string type)
    {
        if (string.IsNullOrEmpty(type)) return "Unknown";
        var t = type.ToLowerInvariant();
        return t switch
        {
            "light_novel" => "Light Novel",
            "novel" => "Novel",
            "one_shot" => "One-shot",
            "doujinshi" => "Doujinshi",
            "manhwa" => "Manhwa",
            "manhua" => "Manhua",
            "oel" => "OEL",
            "manga" => "Manga",
            "tv" => "TV",
            "movie" => "Movie",
            "ova" => "OVA",
            "ona" => "ONA",
            "special" => "Special",
            "music" => "Music",
            _ => char.ToUpper(t[0]) + t.Substring(1)
        };
    }

    [RelayCommand]
    private async Task NavigateToRelation(Models.Entities.AnimeRelation relation)
    {
        if (relation == null || string.IsNullOrEmpty(relation.TargetType)) return;

        var type = relation.TargetType.ToLowerInvariant();
        MediaKind kind;

        if (type == "manga" || type == "manhwa" || type == "manhua" || type == "novel" || type == "light novel" || type == "one-shot" || type == "doujinshi" || type == "light_novel")
        {
            kind = type.Contains("novel") ? MediaKind.LightNovel : MediaKind.Manga;
        }
        else if (type == "anime" || type == "tv" || type == "movie" || type == "ova" || type == "ona" || type == "special")
        {
            kind = MediaKind.Anime;
        }
        else
        {
            kind = MediaKind.Anime;
        }

        var targetAnime = new AnimeItem
        {
            Id = relation.TargetMalId,
            Title = relation.TargetName,
            MediaKind = kind
        };

        // If the item exists in the collection, use the full one to ensure all offline fields are loaded.
        var existing = _animeRepo.Collection.FirstOrDefault(x => x.Id == targetAnime.Id && x.MediaKind == targetAnime.MediaKind);
        await _dialogs.ShowAnimeDetailsAsync(null, existing ?? targetAnime);
    }
}
