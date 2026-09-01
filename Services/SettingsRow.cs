using System.Collections.Generic;

namespace Satisvampory.Services
{
    internal struct SettingsRow
    {
        public SettingsRow()
        {
            DontPullLast = true;
            PullReserve = 10;
            ItemReserves = new Dictionary<string, int>();
            ItemReserveNames = new Dictionary<string, string>();
            ItemCaps = new Dictionary<string, int>();
            ItemCapNames = new Dictionary<string, string>();
            ItemGroups = new Dictionary<string, List<string>>();
            ItemGroupNames = new Dictionary<string, Dictionary<string, string>>();
            DeletedGroups = new List<string>();
            OverlaidGroups = new List<string>();
            PlotSalvage = new Dictionary<string, bool>();
            HeartFeed = new Dictionary<string, bool>();
            HeartFuelSeeded = new List<string>();
            HeartFuelOptOut = new List<string>();
            ClanShareByClan = new Dictionary<string, bool>();
            ClanShareExcludedTerritories = new List<string>();
            StarterKitSeeded = new List<string>();
            StarterKitChestSeeded = new List<string>();
            StarterKitChestOptOut = new List<string>();
            AutoScoop = true;
            AutoFilter = "all";
            NotifyMode = "manual";
            Radius = 10f;
            ScoopMode = "bags";
            ScoopExcludes = new List<string>();
            ScoopExcludeNames = new Dictionary<string, string>();
            ScoopCaps = new Dictionary<string, int>();
            ScoopCapNames = new Dictionary<string, string>();
        }

        public bool SortStash { get; set; }
        public bool Pull { get; set; }
        public bool CraftPull { get; set; }
        public bool DontPullLast { get; set; }
        public int PullReserve { get; set; }
        public Dictionary<string, int> ItemReserves { get; set; }
        public Dictionary<string, string> ItemReserveNames { get; set; }
        public Dictionary<string, int> ItemCaps { get; set; }
        public Dictionary<string, string> ItemCapNames { get; set; }
        public Dictionary<string, List<string>> ItemGroups { get; set; }
        public Dictionary<string, Dictionary<string, string>> ItemGroupNames { get; set; }
        public List<string> DeletedGroups { get; set; }
        public List<string> OverlaidGroups { get; set; }
        public bool AutoStashMissions { get; set; }
        public bool Conveyor { get; set; }
        // Admin: s# chest may fill r# chest that is also s# on the same group. Default OFF.
        public bool ConveyorLoops { get; set; }
        public bool Salvage { get; set; }
        public bool UnitSpawner { get; set; }
        public bool Brazier { get; set; }
        public bool Named { get; set; }
        public bool SilentPull { get; set; }
        public bool SilentStash { get; set; }
        public bool Trash { get; set; }
        // 1.6.1.35: clan-wide RR/.stash. Player default OFF. Server allow default ON.
        public bool RrGlobal { get; set; }
        public bool RrGlobalAllow { get; set; } = true;
        public bool ClanShare { get; set; }
        // Per-plot salvage keyed by territory id. Personal Salvage is unused by ProcessSalvagers.
        public Dictionary<string, bool> PlotSalvage { get; set; }
        // Per-plot heart auto-feed. Missing key = ON by default.
        public Dictionary<string, bool> HeartFeed { get; set; }
        // Persisted heart fuel seed/opt-out keys (heart NetworkId n{net} only; t{plot} ignored).
        public List<string> HeartFuelSeeded { get; set; }
        public List<string> HeartFuelOptOut { get; set; }
        // Clan-wide ClanShare keyed by clan NetworkId ("c{net}"). Missing key = not migrated yet.
        public Dictionary<string, bool> ClanShareByClan { get; set; }
        // Territory ids excluded from clan logistics ("t{id}"). Default not excluded.
        public List<string> ClanShareExcludedTerritories { get; set; }
        // Legacy per-plot starter kit one-shot keyed by t{plot}. Ignored by 1.6.1.31 (false-complete on brick-only).
        public List<string> StarterKitSeeded { get; set; }
        // Per dest-chest NetworkId kit seed/opt-out (n{net} only). Empty after seed = opt-out that chest.
        public List<string> StarterKitChestSeeded { get; set; }
        public List<string> StarterKitChestOptOut { get; set; }
        public bool AutoScoop { get; set; }
        public string AutoFilter { get; set; }
        public string NotifyMode { get; set; }
        public float Radius { get; set; }
        public string ScoopMode { get; set; }
        public List<string> ScoopExcludes { get; set; }
        public Dictionary<string, string> ScoopExcludeNames { get; set; }
        public Dictionary<string, int> ScoopCaps { get; set; }
        public Dictionary<string, string> ScoopCapNames { get; set; }
        public bool AppliedSgAllOn { get; set; }
    }

}
