using UnityEngine;
using System.Collections;
using Satisvampory.Services;
using ProjectM.Scripting;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace Satisvampory;

internal static class Core
{
    static bool booted;
    internal static bool HasInitialized { get { return booted; } }
    internal static void MarkBooted() => booted = true;

    public static void StartCoroutine(IEnumerator routine) => ServerHost.Start(routine);

    public static void LogException(System.Exception e, [CallerMemberName] string caller = null)
    {
        Log.LogError($"Failure in {caller}\nMessage: {e.Message} Inner:{e.InnerException?.Message}\n\nStack: {e.StackTrace}\nInner Stack: {e.InnerException?.StackTrace}");
    }

    public static bool TryGetEntityFromNetworkId(NetworkId networkId, out Entity entity)
    {
        entity = Entity.Null;
        if (!booted)
            return false;
        var lookup = ServerScriptMapper.GetSingleton<NetworkIdSystem.Singleton>();
        return lookup._NetworkIdLookupMap.TryGetValue(networkId, out entity);
    }

    public static void Initialize()
    {
        if (booted)
            return;
        ServerHost.Boot();
    }

    public static World Server { get; } = ServerHost.Find("Server") ?? throw new System.Exception("There is no Server world (yet). Did you install a server mod on the client?");
    public static EntityManager EntityManager { get; } = Server.EntityManager;
    public static GameDataSystem GameDataSystem { get; } = Server.GetExistingSystemManaged<GameDataSystem>();
    public static PrefabCollectionSystem PrefabCollectionSystem { get; internal set; }
    public static ServerGameSettingsSystem ServerGameSettingsSystem { get; internal set; }
    public static ServerScriptMapper ServerScriptMapper { get; internal set; }
    public static DebugEventsSystem DebugEventsSystem { get; internal set; }
    public static double ServerTime { get { return ServerGameManager.ServerTime; } }
    public static ServerGameManager ServerGameManager { get { return ServerScriptMapper.GetServerGameManager(); } }
    public static ManualLogSource Log { get { return Plugin.LogInstance; } }

    public static ConveyorService ConveyorService { get; internal set; }
    public static LocalizationService Localization { get; private set; } = new LocalizationService();
    public static PlayerSettingsService PlayerSettings { get; private set; } = new PlayerSettingsService();
    public static RefinementStationsService RefinementStations { get; internal set; }
    public static RegionService RegionService { get; internal set; }
    public static SalvageService SalvageService { get; internal set; }
    public static StashService Stash { get; private set; } = new StashService();
    public static TrashService Trash { get; private set; } = new TrashService();
    public static TerritoryService TerritoryService { get; internal set; }
    public static UnitSpawnerstationService UnitSpawnerstationService { get; internal set; }
    public static BrazierService BrazierService { get; internal set; }
    public static WorkQueueService WorkQueue { get; internal set; }

    public const int MAX_REPLY_LENGTH = 509;
}
