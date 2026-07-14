using System.Collections.Generic;

namespace Kiriha.Services.Tracking.Anisthesia;

public enum StrategyType
{
    WindowTitle,
    OpenFiles,
    UiAutomation
}

public enum PlayerType
{
    Default,
    WebBrowser
}

public class AnisthesiaPlayer
{
    public string Name { get; set; } = string.Empty;
    public List<string> WindowClasses { get; set; } = new();
    public List<string> Executables { get; set; } = new();
    public List<StrategyType> Strategies { get; set; } = new();
    public string WindowTitleFormat { get; set; } = string.Empty;
    public PlayerType Type { get; set; } = PlayerType.Default;
}

