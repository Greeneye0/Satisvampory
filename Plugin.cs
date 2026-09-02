using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using VampireCommandFramework;

namespace Satisvampory;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("VRisingServer.exe")]
[BepInDependency("gg.deca.VampireCommandFramework")]
public class Plugin : BasePlugin
{
    public static Plugin Instance { get; internal set; }
    public static Harmony Harmony { get; internal set; }
    public static ManualLogSource LogInstance => Instance.Log;
    public HookDOTS.API.HookDOTS hookDOTS;

    public override void Load() => Boot();
    public override bool Unload() => TearDown();

    void Boot()
    {
        Instance = this;
        if (UnityEngine.Application.productName != "VRisingServer") { Log.LogWarning("Satisvampory is a dedicated-server plugin; refusing to load on product '" + UnityEngine.Application.productName + "'."); return; }
        Log.LogInfo($"Satisvampory {MyPluginInfo.PLUGIN_VERSION} ({MyPluginInfo.PLUGIN_GUID}) ready.");
        Services.DestDebugLog.Init();
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Harmony.PatchAll(typeof(Plugin).Assembly);
        CommandRegistry.RegisterAll(typeof(Plugin).Assembly);
        hookDOTS = new HookDOTS.API.HookDOTS(MyPluginInfo.PLUGIN_GUID, this.Log);
        hookDOTS.RegisterAnnotatedHooks();
    }

    bool TearDown() { Services.HuntLoginNote.FlushLogouts(); Core.PlayerSettings?.FlushSettings(force: true); Services.DestDebugLog.Close(); CommandRegistry.UnregisterAssembly(); Harmony?.UnpatchSelf(); return true; }
}
