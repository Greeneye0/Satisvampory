using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

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

    public override void Load()
    {
        Instance = this;
        if (UnityEngine.Application.productName != "VRisingServer")
        {
            Log.LogWarning("Satisvampory is a dedicated-server plugin; refusing to load on product '" + UnityEngine.Application.productName + "'.");
            return;
        }
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} version {MyPluginInfo.PLUGIN_VERSION} is loaded!");
        Services.DestDebugLog.Init();
        Harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        Harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
        CommandRegistry.RegisterAll();
        hookDOTS = new HookDOTS.API.HookDOTS(MyPluginInfo.PLUGIN_GUID, Log);
        hookDOTS.RegisterAnnotatedHooks();
    }

    public override bool Unload()
    {
        Core.PlayerSettings?.FlushSettings(force: true);
        Services.DestDebugLog.Close();
        CommandRegistry.UnregisterAssembly();
        Harmony?.UnpatchSelf();
        return true;
    }
}
