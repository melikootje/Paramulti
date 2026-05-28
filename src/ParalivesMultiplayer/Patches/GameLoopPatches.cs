using System;
using System.Reflection;
using HarmonyLib;

namespace ParalivesMultiplayer.Patches
{
    static class GameLoopPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchSystemManagerUpdate(harmony);
        }

        static void PatchSystemManagerUpdate(Harmony harmony)
        {
            try
            {
                var systemManagerType = null as Type;

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

                if (systemManagerType == null)
                {
                    PatchLogger.LogWarning("SystemManager type not found, cannot hook game update loop.");
                    return;
                }

                var updateMethod = AccessTools.Method(systemManagerType, "Update");
                if (updateMethod == null)
                {
                    PatchLogger.LogWarning("SystemManager.Update method not found.");
                    return;
                }

                PatchLogger.SafePatch(harmony, updateMethod,
                    new HarmonyMethod(typeof(GameLoopPatches), nameof(SystemManagerUpdatePostfix)),
                    "SystemManager.Update");

                PatchLogger.Log("Hooked SystemManager.Update for persistent game loop.");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to patch SystemManager.Update: {ex.Message}");
            }
        }

        static void SystemManagerUpdatePostfix()
        {
            Plugin.OnGameUpdate();
        }
    }
}
