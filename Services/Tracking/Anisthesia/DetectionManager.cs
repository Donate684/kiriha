using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Kiriha.Core;
using Kiriha.Models;
using Kiriha.Services.Data;
using Kiriha.Services.Tracking.Anisthesia.Strategies;
using Serilog;

namespace Kiriha.Services.Tracking.Anisthesia;

public class DetectionManager
{
    private readonly List<AnisthesiaPlayer> _players;
    private readonly SettingsService _settingsService;
    private static readonly Dictionary<string, Regex> RegexCache = new();

    public DetectionManager(List<AnisthesiaPlayer> players, SettingsService settingsService)
    {
        _players = players;
        _settingsService = settingsService;
    }

    private static Regex GetCachedRegex(string pattern)
    {
        if (!RegexCache.TryGetValue(pattern, out var regex))
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexCache[pattern] = regex;
        }
        return regex;
    }

    public async Task<ParsedMedia?> DetectAsync()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var processes = Process.GetProcesses();
        try
        {
            foreach (var proc in processes)
            {
                try
                {
                    string procName = proc.ProcessName;
                    
                    var matchingPlayers = _players.Where(p => 
                        p.Executables.Any(exe => 
                            exe.Equals(procName, StringComparison.OrdinalIgnoreCase) ||
                            (exe.StartsWith("^") && GetCachedRegex(exe).IsMatch(procName))
                        )).ToList();

                    if (matchingPlayers.Count == 0) continue;

                    uint pid = (uint)proc.Id;
                    IntPtr hWnd = proc.MainWindowHandle;

                    foreach (var player in matchingPlayers)
                    {
                        if (_settingsService.Current.System.Scrobbler.AllowedProcesses.Count == 0)
                        {
                            if (player.Type == PlayerType.WebBrowser) continue;
                        }
                        else if (!_settingsService.Current.System.Scrobbler.AllowedProcesses.Contains(player.Name))
                        {
                            continue;
                        }

                        ParsedMedia? result = null;
                        foreach (var strategy in player.Strategies)
                        {
                            if (strategy == StrategyType.OpenFiles)
                                result = HandleEnumerationStrategy.Apply(player, pid);
                            else if (strategy == StrategyType.WindowTitle && hWnd != IntPtr.Zero)
                                result = WindowTitleStrategy.Apply(player, pid, hWnd);

                            if (result != null)
                            {
                                if (IsJunk(result.AnimeTitle, player))
                                {
                                    result = null;
                                    continue;
                                }
                                result.ProcessName = procName;
                                result.Pid = pid;
                                return result;
                            }
                        }
                    }
                }
                catch { /* Access denied or exited */ }
            }
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
        return null;
    }

    public HashSet<string> GetRunningPlayerNames()
    {
        if (!OperatingSystem.IsWindows()) return new HashSet<string>();

        var running = new HashSet<string>();
        var processes = Process.GetProcesses();
        
        try
        {
            var procNames = processes.Select(p => p.ProcessName).ToList();

            foreach (var player in _players)
            {
                if (player.Executables.Any(exe => 
                    procNames.Any(pn => pn.Equals(exe, StringComparison.OrdinalIgnoreCase)) ||
                    (exe.StartsWith("^") && procNames.Any(pn => GetCachedRegex(exe).IsMatch(pn)))))
                {
                    running.Add(player.Name);
                }
            }
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
        return running;
    }

    private bool IsJunk(string title, AnisthesiaPlayer player)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        
        string t = title.Trim().ToLowerInvariant();
        
        // 1. Ignore if title is exactly the player name
        if (t == player.Name.ToLowerInvariant()) return true;

        // 2. Ignore common player "empty" states
        var junkPatterns = new[] { "vlc media player", "mpc-hc", "potplayer", "mpv", "kmplayer", "zoom player", "ready", "opening..." };
        if (junkPatterns.Contains(t)) return true;

        // 3. Ignore very short titles (probably noise)
        if (t.Length < 2) return true;

        // 4. Ignore common system file names if they leaked through
        if (t.EndsWith(".exe") || t.EndsWith(".dll") || t.EndsWith(".ini")) return true;

        return false;
    }
}
