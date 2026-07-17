namespace Kiriha.Models;

public sealed record PlayerMediaMetadata(
    string OriginalTitle,
    string TitleRu,
    string TitleEn,
    string EpisodeText,
    int? AnimeId)
{
    public static PlayerMediaMetadata FromVideoPath(string videoPath)
    {
        var fallbackTitle = string.IsNullOrWhiteSpace(videoPath)
            ? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(videoPath);

        return new PlayerMediaMetadata(fallbackTitle, fallbackTitle, string.Empty, string.Empty, null);
    }
}
