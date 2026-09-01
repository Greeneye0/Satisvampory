using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime;
using Satisvampory.Commands.Converters;
using Satisvampory.Services;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Network;
using ProjectM.Physics;
using ProjectM.Scripting;
using Stunlock.Core;
using System.Collections;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Satisvampory;

internal static class Core
{
    static bool booted;
    internal static bool HasInitialized => booted;
    internal static void MarkBooted() => booted = true;

    public static void StartCoroutine(IEnumerator routine) => ServerHost.Start(routine);

    public static void LogException(System.Exception e, [CallerMemberName] string caller = null) =>
        Log.LogError($"Failure in {caller}\nMessage: {e.Message} Inner:{e.InnerException?.Message}\n\nStack: {e.StackTrace}\nInner Stack: {e.InnerException?.StackTrace}");

    public static bool TryGetEntityFromNetworkId(NetworkId networkId, out Entity entity) { entity = default; return booted && ServerScriptMapper.GetSingleton<NetworkIdSystem.Singleton>()._NetworkIdLookupMap.TryGetValue(networkId, out entity); }

    public static void Initialize() { if (!booted) ServerHost.Boot(); }

    public static World Server { get; } = ServerHost.Find("Server") ?? throw new System.Exception("There is no Server world (yet). Did you install a server mod on the client?");
    public static EntityManager EntityManager { get { return Server.EntityManager; } }
    public static GameDataSystem GameDataSystem { get { return Server.GetExistingSystemManaged<GameDataSystem>(); } }
    public static PrefabCollectionSystem PrefabCollectionSystem { get; set; }
    public static ServerGameSettingsSystem ServerGameSettingsSystem { get; set; }
    public static ServerScriptMapper ServerScriptMapper { get; set; }
    public static DebugEventsSystem DebugEventsSystem { get; set; }
    public static double ServerTime { get { return ServerGameManager.ServerTime; } }
    public static ServerGameManager ServerGameManager { get { return ServerScriptMapper.GetServerGameManager(); } }
    public static ManualLogSource Log { get { return Plugin.LogInstance; } }

    public static ConveyorService ConveyorService { get; set; }
    public static LocalizationService Localization { get; } = new LocalizationService();
    public static PlayerSettingsService PlayerSettings { get; } = new PlayerSettingsService();
    public static RefinementStationsService RefinementStations { get; set; }
    public static RegionService RegionService { get; set; }
    public static SalvageService SalvageService { get; set; }
    public static StashService Stash { get; } = new StashService();
    public static TrashService Trash { get; } = new TrashService();
    public static TerritoryService TerritoryService { get; set; }
    public static UnitSpawnerstationService UnitSpawnerstationService { get; set; }
    public static BrazierService BrazierService { get; set; }
    public static WorkQueueService WorkQueue { get; set; }

    public const int MaxChatReply = 509;
}
