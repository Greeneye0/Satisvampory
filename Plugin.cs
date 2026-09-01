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
    static Plugin plugin;

    Harmony _harmony;
    public static Harmony Harmony => plugin._harmony;
    public static ManualLogSource LogInstance => plugin.Log;

    public HookDOTS.API.HookDOTS hookDOTS;

    public override void Load()
    {
        plugin = this;

        if (UnityEngine.Application.productName != "VRisingServer")
        {
            Log.LogWarning("Satisvampory is a dedicated-server plugin; refusing to load on product '" + UnityEngine.Application.productName + "'.");
            return;
        }

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loaded!");
        Satisvampory.Services.DestDebugLog.Init();

        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

        CommandRegistry.RegisterAll();

        hookDOTS = new HookDOTS.API.HookDOTS(MyPluginInfo.PLUGIN_GUID, Log);
        hookDOTS.RegisterAnnotatedHooks();
    }

    public override bool Unload()
    {
        Core.PlayerSettings?.FlushSettings(force: true);
        CommandRegistry.UnregisterAssembly();
        _harmony?.UnpatchSelf();
        return true;
    }
}
