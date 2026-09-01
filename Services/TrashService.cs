using Satisvampory;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace Satisvampory.Services;

internal sealed class TrashService
{
    public void AshAll(Entity character) => EmptyTrash(character);

    public void EmptyTrash(Entity character) { if (!Gate(character, out var ctx)) return; var cleared = 0; foreach (var bin in Core.Stash.GetAllTrashStashes(ctx.StandingPlot)) if (Wipe(bin)) cleared++; PlayerActionGate.Deny(ctx.User, "Trash emptied from " + cleared.ToString().Color(Color.White) + "x trash containers."); }

    public void EmptyTrash(Entity character, Entity trashContainer) { if (!Gate(character, out var ctx)) return; Wipe(trashContainer); PlayerActionGate.Deny(ctx.User, "Sunlight, at this hour, in this castle, localized entirely within this trash bin? This trash is ashed."); }

    static bool Gate(Entity character, out PlayerActionGate.Context ctx)
    {
        ctx = default;
        if (!PlayerActionGate.TryOpen(character, "empty trash", requireAlliedHeart: true, out ctx, out var deny)) { if (ctx.UserEntity != Entity.Null) PlayerActionGate.Deny(ctx.User, deny); return false; }
        if (!Core.PlayerSettings.IsTrashEnabled()) return true;
        PlayerActionGate.Deny(ctx.User, "Trash is globally disabled.");
        return false;
    }

    static bool Wipe(Entity container)
    {
        if (!InventoryUtilities.TryGetInventoryEntity(Core.EntityManager, container, out var inventory))
            return false;
        var slots = Core.EntityManager.GetBuffer<InventoryBuffer>(inventory);
        var any = false;
        for (var i = 0; i < slots.Length; i++)
            if (slots[i].Amount > 0) { InventoryUtilitiesServer.ClearSlot(Core.EntityManager, inventory, i); any = true; }
        return any;
    }
}
