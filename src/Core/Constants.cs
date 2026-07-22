using Kiriha.Core.Shiki;
namespace Kiriha.Core;

public static class Constants
{
    public static class Api
    {
        public const string RedirectUri = "http://localhost:8080/";
        // NOTE: UserAgent moved to Kiriha.Core.AppInfo (auto-resolved from assembly version).
        // NOTE: MAL client_id is at Kiriha.Core.ApiKeys.MalClientId — do not re-add an alias.

        public static class Mal
        {
            public const string BaseUrl = "https://api.myanimelist.net/v2/";
            public const string TokenUrl = "https://myanimelist.net/v1/oauth2/token";
            public const string AuthUrl = "https://myanimelist.net/v1/oauth2/authorize";
            public const string WebsiteUrl = "https://myanimelist.net/anime/";
        }

        // shikimori.one and shikimori.net are independent OAuth realms with identical
        // contracts. We collapse the duplicated four-URL block into a single record
        // and expose two named instances. ShikiEndpoints.cs picks one based on the
        // active mirror in settings; OAuth token exchange runs directly from the
        // user's machine (see ApiKeys.cs for the WAF-bypass rationale).
        public static class Shiki
        {
            public static readonly ShikiHost One = new(
                BaseUrl: "https://shikimori.one/api/",
                TokenUrl: "https://shikimori.one/oauth/token",
                AuthUrl: "https://shikimori.one/oauth/authorize",
                WebsiteUrl: "https://shikimori.one/animes/",
                MangaWebsiteUrl: "https://shikimori.one/mangas/");

            public static readonly ShikiHost Net = new(
                BaseUrl: "https://shikimori.net/api/",
                TokenUrl: "https://shikimori.net/oauth/token",
                AuthUrl: "https://shikimori.net/oauth/authorize",
                WebsiteUrl: "https://shikimori.net/animes/",
                MangaWebsiteUrl: "https://shikimori.net/mangas/");
        }
    }

    public static class Links
    {
        public const string GitHubRepo = "https://github.com/donate684/kiriha";
        public const string GitHubReleases = "https://github.com/donate684/kiriha/releases";
    }

    public static class AnimeTypes
    {
        public const string Tv = "tv";
        public const string Movie = "movie";
        public const string Ova = "ova";
        public const string Ona = "ona";
        public const string Special = "special";
        public const string TvSpecial = "tv_special";
    }

    public static class Sorting
    {
        public const string Popularity = "Popularity";
        public const string Score = "Score";
        public const string Title = "Title";
        public const string RussianTitle = "RussianTitle";
        public const string Date = "Date";
    }

    public static class Seasons
    {
        public const string Winter = "winter";
        public const string Spring = "spring";
        public const string Summer = "summer";
        public const string Fall = "fall";
    }

    public static class Languages
    {
        public const string En = "en";
        public const string Ru = "ru";
        public const string EnName = "English";
        public const string RuName = "Русский";
    }

    public static class System
    {
        public const string AppName = "Kiriha";
        public const string MutexName = "Kiriha_SingleInstance_Mutex";
        public const string AppStartedLog = "--- Application Starting ---";

        public static class FileNames
        {
            public const string Database = "kiriha.db";
            public const string Settings = "config.json";
            public const string Mappings = "title_mappings.json";
            public const string LogsDir = "logs";
            public const string CacheDir = "cacheimg";
        }
    }
}
