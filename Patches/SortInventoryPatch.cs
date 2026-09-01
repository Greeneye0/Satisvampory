
namespace Satisvampory.Patches;

[HarmonyPatch]
public class SortSingleInventorySystemPatch
{
    static readonly Dictionary<ulong, double> stashArmed = new();
    static readonly Dictionary<ulong, double> trashArmed = new();

    [HarmonyPatch(typeof(SortSingleInventorySystem), nameof(SortSingleInventorySystem.OnUpdate))]
    [HarmonyPrefix]
    static void Prefix(SortSingleInventorySystem __instance) => Drain(__instance);

    static void Drain(SortSingleInventorySystem system)
    {
        var rows = system._EventQuery.ToEntityArray(Allocator.Temp);
        try
        {
            var now = Core.ServerTime;
            DropStale(stashArmed, now);
            DropStale(trashArmed, now);
            for (var i = 0; i < rows.Length; i++)
                Handle(rows[i], now);
        }
        finally { rows.Dispose(); }
    }

    static void Handle(Entity entity, double now)
    {
        if (entity.Equals(Entity.Null)) return;
        var from = entity.Read<FromCharacter>();
        var sort = entity.Read<SortSingleInventoryEvent>();
        var steam = from.User.Read<User>().PlatformId;
        if (sort.Inventory == from.Character.Read<NetworkId>())
        {
            if (Core.PlayerSettings.IsSortStashEnabled(steam) && Armed(stashArmed, steam, now))
                Core.Stash.StashCharacterInventory(from.Character);
            return;
        }
        if (!Armed(trashArmed, steam, now)) return;
        var plot = Core.TerritoryService.GetTerritoryId(from.Character);
        foreach (var trash in Core.Stash.GetAllTrashStashes(plot))
        {
            if (trash.Read<NetworkId>() != sort.Inventory) continue;
            Core.Trash.EmptyTrash(from.Character, trash);
            break;
        }
    }

    static bool Armed(Dictionary<ulong, double> clicks, ulong steam, double now)
    {
        if (clicks.TryGetValue(steam, out var armedAt) && now - armedAt < 1)
        {
            clicks.Remove(steam);
            return true;
        }
        clicks[steam] = now;
        return false;
    }

    static void DropStale(Dictionary<ulong, double> clicks, double now)
    {
        if (clicks.Count == 0) return;
        List<ulong> stale = null;
        foreach (var kv in clicks)
            if (now - kv.Value >= 1)
                (stale ??= new List<ulong>()).Add(kv.Key);
        if (stale == null) return;
        for (var i = 0; i < stale.Count; i++)
            clicks.Remove(stale[i]);
    }
}
