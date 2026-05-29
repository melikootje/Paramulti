using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class GameLoopPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchSystemManagerLateUpdate(harmony);
        }

        static void PatchSystemManagerLateUpdate(Harmony harmony)
        {
            try
            {
                var systemManagerType = ParalivesGameApiResolver.SystemManagerType;
                if (systemManagerType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var name = asm.GetName().Name;
                        if (name == null) continue;

                        if (name.Contains("Paralives"))
                        {
                            systemManagerType = asm.GetType("SystemManager");
                            if (systemManagerType != null) break;
                        }
                    }
                }

                if (systemManagerType == null)
                {
                    PatchLogger.LogWarning("SystemManager type not found, cannot hook game update loop.");
                    return;
                }

                var lateUpdateMethod = AccessTools.Method(systemManagerType, "LateUpdate");
                if (lateUpdateMethod == null)
                {
                    PatchLogger.LogWarning("SystemManager.LateUpdate method not found.");
                    return;
                }

                PatchLogger.SafePatch(harmony, lateUpdateMethod,
                    new HarmonyMethod(typeof(GameLoopPatches), nameof(SystemManagerLateUpdatePostfix)),
                    "SystemManager.LateUpdate");

                PatchLogger.Log("Hooked SystemManager.LateUpdate for persistent game loop.");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to patch SystemManager.LateUpdate: {ex.Message}");
            }
        }

        static void SystemManagerLateUpdatePostfix()
        {
            // Plugin.Update() already calls Plugin.OnGameUpdate() every frame.
            // This hook is kept as a successful integration point with the game's core loop.
            // Future game-frame-specific logic can be added here if needed.
        }
    }
}
