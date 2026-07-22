using System.Collections.Generic;

namespace Kiriha.Models;

public partial class AppSettings
{
    public class TorrentConfig
    {
        public bool OnlyCrunchyroll { get; set; } = false;
        public bool FilterNetflix { get; set; } = false;
        public bool FilterAmazon { get; set; } = false;
        public bool FilterHidive { get; set; } = false;
        public bool FilterVaryg { get; set; } = false;
        public bool FilterEraiRaws { get; set; } = false;
        public bool FilterToonsHub { get; set; } = false;
        public bool FilterHevc { get; set; } = false;
        public bool Filter1080p { get; set; } = false;
        public System.Collections.Generic.List<int> HiddenAnimeIds { get; set; } = new();

        /// <summary>When true, filter toggles are remembered per anime title.</summary>
        public bool FiltersPerTitle { get; set; } = false;
        public System.Collections.Generic.Dictionary<int, TorrentFilterSet> PerTitleFilters { get; set; } = new();
    }

    public class TorrentFilterSet
    {
        public bool OnlyCrunchyroll { get; set; }
        public bool FilterNetflix { get; set; }
        public bool FilterAmazon { get; set; }
        public bool FilterHidive { get; set; }
        public bool FilterVaryg { get; set; }
        public bool FilterEraiRaws { get; set; }
        public bool FilterToonsHub { get; set; }
        public bool FilterHevc { get; set; }
        public bool Filter1080p { get; set; }
    }
}
