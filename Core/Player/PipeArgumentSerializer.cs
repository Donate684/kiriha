using Kiriha.Core.Infrastructure;
using Kiriha.Core.Platform;
using Kiriha.Core.Player;
using Kiriha.Core.Shiki;
using System;
using System.Text.Json;

namespace Kiriha.Core.Player;

public static class PipeArgumentSerializer
{
    public static string Serialize(string[] args) => JsonSerializer.Serialize(args);

    public static string[] Deserialize(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return line.Split("||", StringSplitOptions.None);
        }
    }
}
