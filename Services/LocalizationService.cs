using ProjectM.Shared;
using Stunlock.Localization;

namespace Satisvampory.Services;

internal class LocalizationService
{
    readonly Dictionary<string, string> localization;
    readonly Dictionary<int, string> prefabNames;
    static readonly HashSet<int> BookTier1 = [1455590675, -651642571];
    static readonly HashSet<int> BookTier2 = [1150376281, 686122001];
    const int DarkmatterPistols = -1265586439;

    public LocalizationService()
    {
        localization = EmbeddedJson.Load<string, string>("Satisvampory.Localization.English.json");
        prefabNames = EmbeddedJson.Load<int, string>("Satisvampory.Data.PrefabNames.json");
    }

    public string GetLocalization(string guid) =>
        localization.TryGetValue(guid, out var text) ? text : $"<Localization not found for {guid}>";

    public string GetLocalization(LocalizationKey key) => GetLocalization(key.Key.ToGuid().ToString());

    public string GetPrefabName(PrefabGUID itemPrefabGUID)
    {
        if (!prefabNames.TryGetValue(itemPrefabGUID._Value, out var locKey))
            return null;
        var label = GetLocalization(locKey);
        if (itemPrefabGUID._Value == DarkmatterPistols)
            label = "Darkmatter Pistols";
        label = Annotate(itemPrefabGUID, label);
        if (BookTier1.Contains(itemPrefabGUID._Value)) return label + " Tier 1";
        if (BookTier2.Contains(itemPrefabGUID._Value)) return label + " Tier 2";
        return label;
    }

    static string Annotate(PrefabGUID guid, string label)
    {
        if (!Core.PrefabCollectionSystem._PrefabLookupMap.TryGetValue(guid, out var prefab))
            return label;
        if (prefab.Has<ItemData>() && prefab.Read<ItemData>().ItemType == ItemType.Tech)
            label = "Book " + label;
        if (prefab.Has<JewelInstance>() && prefab.Read<JewelInstance>().TierIndex != 0)
            label += $" Jewel Tier {prefab.Read<JewelInstance>().TierIndex + 1}";
        if (prefab.Has<LegendaryItemInstance>())
            label += $" Tier {prefab.Read<LegendaryItemInstance>().TierIndex + 1}";
        if (prefab.Has<ShatteredItem>())
            label += " Shattered";
        return label;
    }
}
