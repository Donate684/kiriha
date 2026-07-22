using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Serilog;

namespace Kiriha.Services.Data;

public class LocalizationService
{
    private string _currentLanguage = "en";
    private readonly string[] _namespaces =
    {
        "common", "navigation", "anime", "genres", "settings",
        "scrobbler", "filters", "history", "wizard", "sync",
        "updates", "auth", "search", "torrents", "notifications", "crash",
        "about"
    };

    public void LoadLanguage(string langCode)
    {
        try
        {
            _currentLanguage = langCode;
            var resources = new Dictionary<string, string>();

            // 1. Load English as base (fallback)
            LoadAllNamespaces("en", resources);

            // 2. If not English, load and override
            if (langCode != "en")
            {
                LoadAllNamespaces(langCode, resources);
            }

            // 3. Inject into Avalonia resources
            if (Application.Current != null)
            {
                foreach (var kvp in resources)
                {
                    Application.Current.Resources[$"l.{kvp.Key}"] = kvp.Value;
                }
            }

            Log.Information("Language loaded: {Lang} ({Count} keys)", langCode, resources.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load language: {Lang}", langCode);
        }
    }

    private void LoadAllNamespaces(string langCode, Dictionary<string, string> target)
    {
        foreach (var ns in _namespaces)
        {
            var nsStrings = LoadNamespace(langCode, ns);
            foreach (var kvp in nsStrings)
            {
                target[kvp.Key] = kvp.Value;
            }
        }
    }

    private Dictionary<string, string> LoadNamespace(string langCode, string ns)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var uri = new Uri($"avares://Kiriha/Assets/i18n/{langCode}/{ns}.json");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            using var doc = JsonDocument.Parse(json);
            FlattenJson(doc.RootElement, ns, result);
        }
        catch (FileNotFoundException)
        {
            Log.Debug("Namespace {Namespace} not found for language {Lang}", ns, langCode);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to load namespace {Namespace} for {Lang}: {Error}", ns, langCode, ex.Message);
        }
        return result;
    }

    private void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    FlattenJson(property.Value, string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}", result);
                }
                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    FlattenJson(item, $"{prefix}[{index}]", result);
                    index++;
                }
                break;
            default:
                result[prefix] = element.ToString();
                break;
        }
    }
}
