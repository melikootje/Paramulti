using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class GameSavingPatches
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                var saveMgrType = ParalivesGameApiResolver.ResolveType("GameSavingManager");
                if (saveMgrType == null)
                {
                    PatchLogger.LogWarning("GameSavingManager not found, skipping save patches");
                    return;
                }

                var createRequest = AccessTools.Method(saveMgrType, "CreateRequest", new System.Type[] { });
                if (createRequest != null)
                {
                    PatchLogger.SafePatch(harmony, createRequest,
                        new HarmonyMethod(typeof(GameSavingPatches), nameof(OnCreateRequestPostfix)),
                        "GameSavingManager.CreateRequest()");
                }

                var createRequestFull = AccessTools.Method(saveMgrType, "CreateRequest",
                    new System.Type[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                if (createRequestFull != null)
                {
                    PatchLogger.SafePatch(harmony, createRequestFull,
                        new HarmonyMethod(typeof(GameSavingPatches), nameof(OnCreateRequestPostfix)),
                        "GameSavingManager.CreateRequest(bool,bool,bool,bool)");
                }

                PatchLogger.Log("[Save] Patched GameSavingManager.CreateRequest for remote character cleanup");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"[Save] Failed to patch GameSavingManager: {ex.Message}");
            }
        }

        static void OnCreateRequestPostfix()
        {
            try
            {
                if (!MultiplayerSession.IsActive) return;

                Plugin.Log.LogInfo("[Save] Save request detected — removing remote characters from household before save");
                HouseholdSyncManager.RemoveAllRemoteCharactersFromHousehold();
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"[Save] Cleanup error: {ex.Message}");
            }
        }
    }
}
