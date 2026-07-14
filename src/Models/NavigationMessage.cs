namespace Kiriha.Models;

public enum NavigationPage
{
    Home,
    AnimeList,
    Profile,
    Seasonal,
    History,
    Torrents,
    Search,
    Settings,
    Welcome
}

public class NavigationMessage
{
    public NavigationPage Page { get; }
    public NavigationMessage(NavigationPage page) => Page = page;
}

