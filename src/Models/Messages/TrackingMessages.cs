using Kiriha.Models.Entities;
using Kiriha.Utils.Parsing;

namespace Kiriha.Models.Messages;

public record MediaChangedMessage(ParsedMedia? Media);

public record AnimeMatchedMessage(AnimeItem? Anime);

public record TrackingCountdownMessage(string Countdown);

public record TrackingStatusMessage(string Status);
