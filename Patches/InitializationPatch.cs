using Unity.Scenes;

namespace Satisvampory.Patches;

[HarmonyPatch(typeof(SceneSectionStreamingSystem), nameof(SceneSectionStreamingSystem.ShutdownAsynchrnonousStreamingSupport))]
public static class InitializationPatch
{
    [HarmonyPostfix]
    public static void AfterSceneReady()
    {
        Core.Initialize();
        Plugin.Harmony.Unpatch(
            typeof(SceneSectionStreamingSystem).GetMethod(nameof(SceneSectionStreamingSystem.ShutdownAsynchrnonousStreamingSupport)),
            typeof(InitializationPatch).GetMethod(nameof(AfterSceneReady)));
    }
}
