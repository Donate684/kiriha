using System;
using System.Collections.Generic;

namespace Kiriha.Services.Tracking.Anisthesia;

public class PlayerParser
{
    private enum State
    {
        ExpectPlayerName,
        ExpectSection,
        ExpectWindow,
        ExpectExecutable,
        ExpectStrategy,
        ExpectType,
        ExpectWindowTitle,
    }

    private static int GetIndentation(string line)
    {
        int count = 0;
        foreach (char c in line)
        {
            if (c == '\t') count++;
            else break;
        }
        return count;
    }

    public static List<AnisthesiaPlayer> ParseData(string data)
    {
        var players = new List<AnisthesiaPlayer>();
        var lines = data.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None);
        AnisthesiaPlayer? current = null;
        State state = State.ExpectPlayerName;

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            int indent = GetIndentation(rawLine);
            string line = rawLine.Trim();
            if (line.StartsWith("#")) continue;

            if (indent == 0)
            {
                current = new AnisthesiaPlayer { Name = line };
                players.Add(current);
                state = State.ExpectSection;
                continue;
            }

            if (current == null) continue;

            if (indent == 1)
            {
                line = line.TrimEnd(':');
                state = line switch
                {
                    "windows" => State.ExpectWindow,
                    "executables" => State.ExpectExecutable,
                    "strategies" => State.ExpectStrategy,
                    "type" => State.ExpectType,
                    _ => State.ExpectSection
                };
                continue;
            }

            if (indent == 2)
            {
                if (state == State.ExpectWindow) current.WindowClasses.Add(line);
                else if (state == State.ExpectExecutable) current.Executables.Add(line);
                else if (state == State.ExpectStrategy)
                {
                    line = line.TrimEnd(':');
                    if (line == "window_title")
                    {
                        current.Strategies.Add(StrategyType.WindowTitle);
                        state = State.ExpectWindowTitle;
                    }
                    else if (line == "open_files") current.Strategies.Add(StrategyType.OpenFiles);
                    else if (line == "ui_automation") current.Strategies.Add(StrategyType.UiAutomation);
                }
                else if (state == State.ExpectType)
                {
                    current.Type = line == "web_browser" ? PlayerType.WebBrowser : PlayerType.Default;
                }
                continue;
            }

            if (indent == 3 && state == State.ExpectWindowTitle)
            {
                current.WindowTitleFormat = line;
            }
        }
        return players;
    }
}
