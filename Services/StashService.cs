using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using System.Linq;
using System.Text;

namespace Satisvampory.Services
{
    internal class StashService
    {
        public const string SPOILS_SUFFIX = "spoils";
        public static readonly PrefabGUID ExternalInventoryPrefab = new(1183666186);

        public delegate bool StashFilter(Entity station);

        readonly Regex receiverRegex;
        readonly Regex senderRegex;
        readonly ChestIndex chests;
        readonly FindSpotlight spotlight = new();
        readonly FindReport finder;

        public Regex ReceiverRegex => receiverRegex;
        public Regex SenderRegex => senderRegex;

        const float STASH_COOLDOWN = 1f;
        readonly Dictionary<Entity, double> lastStashed = [];

        public StashService()
        {
            receiverRegex = new Regex(Const.RECEIVER_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            senderRegex = new Regex(Const.SENDER_REGEX, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            chests = new ChestIndex(senderRegex, receiverRegex);
            finder = new FindReport(spotlight);
        }

        internal void InvalidateTerritory(int territoryId) => chests.Forget(territoryId);
        internal void InvalidateAllStashLists() => chests.ForgetAll();

        /// <summary>
        /// Kindred conveyor chests: name matches s# sender or r# receiver regex
        /// (same Const.SENDER_REGEX / RECEIVER_REGEX used for conveyors).
        /// Unnamed and other labels (salvage, overflow, player names, etc.) are false.
        /// </summary>
        public bool IsConveyorNamedStash(Entity stash)
        {
            if (stash == Entity.Null || !Core.EntityManager.Exists(stash) || !stash.Has<NameableInteractable>())
                return false;
            var name = stash.Read<NameableInteractable>().Name.ToString().ToLower();
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return senderRegex.IsMatch(name) || receiverRegex.IsMatch(name);
        }

        public IEnumerable<Entity> GetAllAlliedStashesOnTerritory(Entity character)
        {
            foreach (var territoryId in Core.TerritoryService.GetLogisticsTerritoryIdsForCharacter(character))
            {
                var heart = Core.TerritoryService.GetCastleHeart(territoryId);
                if (heart == Entity.Null) continue;
                if (!Core.ServerGameManager.IsAllies(heart, character)) continue;
                if (TerritoryService.IsHeartRaided(heart)) continue;
                foreach (var stash in GetStashesOnTerritory(territoryId))
                    yield return stash;
            }
        }

        public IEnumerable<int> GetOverflowTerritoryIds(int standingTerritoryId)
        {
            return Core.TerritoryService.GetLogisticsTerritoryIds(standingTerritoryId);
        }

        public IEnumerable<Entity> GetStashesOnTerritory(int territoryIndex) => chests.OnPlot(territoryIndex);
        public IEnumerable<(int group, Entity station)> GetAllReceivingStashes(int territoryId) => chests.Receivers(territoryId);
        public IEnumerable<(int group, Entity station)> GetAllSendingStashes(int territoryId) => chests.Senders(territoryId);
        public IEnumerable<Entity> GetAllSalvageStashes(int territoryId) => chests.Named(territoryId, "salvage");
        public IEnumerable<Entity> GetAllSpawnerStashes(int territoryId) => chests.Named(territoryId, "spawner");
        public IEnumerable<Entity> GetAllBrazierStashes(int territoryId) => chests.Named(territoryId, "brazier");
        public IEnumerable<Entity> GetAllOverflowStashes(int territoryId) => chests.Named(territoryId, "overflow");
        public IEnumerable<Entity> GetAllTrashStashes(int territoryId) => chests.Named(territoryId, "trash");

        public void StashCharacterInventory(Entity charEntity)
        {
            try { DepositNow(charEntity); }
            catch (System.Exception e) { Core.LogException(e, "Stash Character Inventory"); }
        }

        void DepositNow(Entity charEntity)
        {
            if (charEntity == Entity.Null || !Core.EntityManager.Exists(charEntity) || !charEntity.Has<PlayerCharacter>())
                return;
            var user = charEntity.Read<PlayerCharacter>().UserEntity.Read<User>();
            if (lastStashed.TryGetValue(charEntity, out var lastStashTime) && Core.ServerTime - lastStashTime < STASH_COOLDOWN)
            {
                Utilities.SendSystemMessageToClient(Core.EntityManager, user, "You must wait before stashing again!");
                return;
            }
            if (!PlayerActionGate.TryOpenForStash(charEntity, out var ctx, out var deny))
            {
                if (ctx.UserEntity != Entity.Null) PlayerActionGate.Deny(ctx.User, deny);
                return;
            }
            lastStashed[charEntity] = Core.ServerTime;
            PlayerDeposit.FromCharacter(charEntity, ctx);
        }

        public void AdminStash(Entity charEntity, PrefabGUID itemType, int amountToGive) =>
            AdminGive.IntoPlot(charEntity, itemType, amountToGive);

        public void ReportWhereItemIsLocated(Entity charEntity, PrefabGUID item)
            => finder.Items(charEntity, item);

        public void ReportWhereChestIsLocated(Entity charEntity, string chestName)
            => finder.Chests(charEntity, chestName);

        public int StashServantLoot(Entity servant) => ServantLoot.Deposit(servant);

        internal string DebugListServants(int plotFilter) => ServantLoot.List(plotFilter);

        internal string DebugStashAllServants(int plotFilter) => ServantLoot.StashAll(plotFilter);
    }
}
