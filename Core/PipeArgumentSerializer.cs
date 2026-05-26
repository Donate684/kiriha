using System;
using System.Text.Json;

namespace Kiriha.Core;

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
