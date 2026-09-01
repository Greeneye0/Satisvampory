using Il2CppInterop.Runtime;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using Stunlock.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Satisvampory.Services;

internal class StashService
{
    public const string SpoilsLabel = "spoils";
    public static readonly PrefabGUID ChestBagGuid = new(1183666186);

    readonly Regex receiveRx;
    readonly Regex sendRx;
    readonly ChestIndex chests;
    readonly FindSpotlight spotlight = new();
    readonly FindReport finder;
    const float StashCooldown = 1f;
    readonly Dictionary<Entity, double> lastStashed = [];

    public Regex ReceiveToken => receiveRx;
    public Regex SendToken => sendRx;

    public StashService() { receiveRx = new Regex(BeltTokens.Receiver, RegexOptions.Compiled | RegexOptions.IgnoreCase); sendRx = new Regex(BeltTokens.Sender, RegexOptions.Compiled | RegexOptions.IgnoreCase); chests = new ChestIndex(sendRx, receiveRx); finder = new FindReport(spotlight); }

    internal void InvalidateTerritory(int territoryId) => chests.Forget(territoryId);
    internal void InvalidateAllStashLists() => chests.ForgetAll();

    public bool HasBeltToken(Entity stash) { if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>()) return false; var plate = stash.Read<NameableInteractable>().Name.ToString().ToLower(); return !string.IsNullOrWhiteSpace(plate) && (sendRx.IsMatch(plate) || receiveRx.IsMatch(plate)); }

    public IEnumerable<Entity> IslandChests(Entity character)
    {
        foreach (var territoryId in Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character)) { var heart = Core.TerritoryService.GetCastleHeart(territoryId); if (heart == Entity.Null || !Core.ServerGameManager.IsAllies(heart, character) || TerritoryService.IsHeartRaided(heart)) continue; foreach (var stash in ChestsOnPlot(territoryId)) yield return stash; }
    }

    public IEnumerable<int> IslandPlotIds(int standingTerritoryId) =>
        Core.TerritoryService.GetLogisticsTerritoryIds(standingTerritoryId);

    public IEnumerable<Entity> ChestsOnPlot(int territoryIndex) => chests.OnPlot(territoryIndex);
    public IEnumerable<(int group, Entity station)> ReceiveChests(int territoryId) => chests.Receivers(territoryId);
    public IEnumerable<(int group, Entity station)> SendChests(int territoryId) => chests.Senders(territoryId);
    public IEnumerable<Entity> SalvageChests(int territoryId) => chests.Named(territoryId, "salvage");
    public IEnumerable<Entity> SpawnerChests(int territoryId) => chests.Named(territoryId, "spawner");
    public IEnumerable<Entity> BrazierChests(int territoryId) => chests.Named(territoryId, "brazier");
    public IEnumerable<Entity> OverflowChests(int territoryId) => chests.Named(territoryId, "overflow");
    public IEnumerable<Entity> TrashChests(int territoryId) => chests.Named(territoryId, "trash");

    public void StashCharacterInventory(Entity charEntity)
    {
        try { DepositNow(charEntity); }
        catch (System.Exception e) { Core.LogException(e, "Stash Character Inventory"); }
    }

    void DepositNow(Entity charEntity)
    {
        if (charEntity == Entity.Null || !Core.EntityManager.Exists(charEntity) || !charEntity.Has<PlayerCharacter>()) return;
        var user = charEntity.Read<PlayerCharacter>().UserEntity.Read<User>();
        if (lastStashed.TryGetValue(charEntity, out var lastStashTime) && Core.ServerTime - lastStashTime < StashCooldown)
        { Utilities.SendSystemMessageToClient(Core.EntityManager, user, "Wait a moment before stashing again."); return; }
        if (!PlayerActionGate.TryOpenForStash(charEntity, out var ctx, out var deny))
        { if (ctx.UserEntity != Entity.Null) PlayerActionGate.Deny(ctx.User, deny); return; }
        lastStashed[charEntity] = Core.ServerTime;
        PlayerDeposit.FromCharacter(charEntity, ctx);
    }

    public void AdminStash(Entity charEntity, PrefabGUID itemType, int amountToGive) =>
        AdminGive.IntoPlot(charEntity, itemType, amountToGive);

    public void ReportWhereItemIsLocated(Entity charEntity, PrefabGUID item) => finder.Items(charEntity, item);
    public void ReportWhereChestIsLocated(Entity charEntity, string chestName) => finder.Chests(charEntity, chestName);
    internal void ShowItems(Entity character, PrefabGUID item) => finder.Items(character, item);
    internal void ShowChests(Entity character, string name) => finder.Chests(character, name);
    public int StashServantLoot(Entity servant) => ServantLoot.Deposit(servant);
    internal string DebugListServants(int plotFilter) => ServantLoot.List(plotFilter);
    internal string DebugStashAllServants(int plotFilter) => ServantLoot.StashAll(plotFilter);
}
