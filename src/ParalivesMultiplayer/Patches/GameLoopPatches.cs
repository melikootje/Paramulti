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
            // Hook SystemManager.LateUpdate to drive the mod's game loop.
            // This runs regardless of whether BaseUnityPlugin GameObject is destroyed.
            PatchSystemManagerLateUpdate(harmony);
        }

        static void PatchSystemManagerLateUpdate(Harmony harmony)
        {
            try
            {
                var systemManagerType = ParalivesGameApiResolver.SystemManagerType;
                if (systemManagerType == null)
                {
                    systemManagerType = ParalivesGameApiResolver.FindTypeBySimpleName("SystemManager");
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
            // Call Plugin.OnGameUpdate each frame to keep networking, input, and HUD alive
            // even when BaseUnityPlugin's own Update() is destroyed after Awake.
            Plugin.OnGameUpdate();
        }
    }
}
