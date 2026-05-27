using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class SceneManagementPatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchUnitySceneManager(harmony);
        }

        static void PatchUnitySceneManager(Harmony harmony)
        {
            try
            {
                var sceneManagerType = AccessTools.TypeByName("UnityEngine.SceneManagement.SceneManager");
                if (sceneManagerType == null)
                {
                    PatchLogger.LogWarning("UnityEngine.SceneManagement.SceneManager not found, skipping scene patches.");
                    return;
                }

                var loadSceneInt = AccessTools.Method(sceneManagerType, "LoadScene", new Type[] { typeof(int) });
                if (loadSceneInt != null)
                {
                    PatchLogger.SafePatch(harmony, loadSceneInt,
                        new HarmonyMethod(typeof(SceneManagementPatches), nameof(OnLoadScenePostfix)),
                        "SceneManager.LoadScene(int)");
                }

                var loadSceneStr = AccessTools.Method(sceneManagerType, "LoadScene", new Type[] { typeof(string) });
                if (loadSceneStr != null)
                {
                    PatchLogger.SafePatch(harmony, loadSceneStr,
                        new HarmonyMethod(typeof(SceneManagementPatches), nameof(OnLoadSceneByNamePostfix)),
                        "SceneManager.LoadScene(string)");
                }

                var asyncLoadScene = AccessTools.Method(sceneManagerType, "LoadSceneAsync", new Type[] { typeof(int) });
                if (asyncLoadScene != null)
                {
                    PatchLogger.SafePatch(harmony, asyncLoadScene,
                        new HarmonyMethod(typeof(SceneManagementPatches), nameof(OnLoadSceneAsyncPostfix)),
                        "SceneManager.LoadSceneAsync(int)");
                }
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply scene management patches: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void OnLoadScenePostfix(int sceneBuildIndex, object __instance)
        {
            if (!MultiplayerSession.IsActive) return;

            PatchLogger.Log($"Scene loading (index): {sceneBuildIndex}");

            if (MultiplayerSession.IsHost && TcpNetworkManager.Instance != null)
            {
                var msg = new MsgSyncState
                {
                    Tick = MultiplayerSession.Tick,
                    PlayerCount = MultiplayerSession.PlayerCount
                };
                TcpNetworkManager.Instance.SendToAllClients(msg);
                PatchLogger.LogDebug($"Host broadcasted SyncState after scene load (index={sceneBuildIndex})");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void OnLoadSceneByNamePostfix(string sceneName, object __instance)
        {
            if (!MultiplayerSession.IsActive) return;

            PatchLogger.Log($"Scene loading (name): {sceneName}");

            if (MultiplayerSession.IsHost && TcpNetworkManager.Instance != null)
            {
                var msg = new MsgSyncState
                {
                    Tick = MultiplayerSession.Tick,
                    PlayerCount = MultiplayerSession.PlayerCount
                };
                TcpNetworkManager.Instance.SendToAllClients(msg);
                PatchLogger.LogDebug($"Host broadcasted SyncState after scene load (name={sceneName})");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void OnLoadSceneAsyncPostfix(int sceneBuildIndex, object __instance)
        {
            if (!MultiplayerSession.IsActive) return;

            PatchLogger.Log($"Scene loading async (index): {sceneBuildIndex}");
        }
    }
}
