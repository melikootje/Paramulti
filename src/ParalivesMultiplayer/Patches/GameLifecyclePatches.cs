using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class GameLifecyclePatches
    {
        public static void Apply(Harmony harmony)
        {
            PatchGameManager(harmony);
        }

        static void PatchGameManager(Harmony harmony)
        {
            try
            {
                var gameManagerType = ParalivesGameApiResolver.FindTypeBySimpleName("GameManager");
                if (gameManagerType == null)
                    gameManagerType = ParalivesGameApiResolver.FindTypeByPartialName("GameManager");

                if (gameManagerType == null)
                {
                    PatchLogger.LogWarning("GameManager type not found, skipping lifecycle patches.");
                    return;
                }

                var returnToMenu = AccessTools.Method(gameManagerType, "ReturnToMenu");
                if (returnToMenu != null)
                {
                    PatchLogger.SafePatchPrefix(harmony, returnToMenu,
                        new HarmonyMethod(typeof(GameLifecyclePatches), nameof(OnReturnToMenuPrefix)),
                        "GameManager.ReturnToMenu");
                }

                var restartLevel = AccessTools.Method(gameManagerType, "RestartLevel");
                if (restartLevel != null)
                {
                    PatchLogger.SafePatchPrefix(harmony, restartLevel,
                        new HarmonyMethod(typeof(GameLifecyclePatches), nameof(OnRestartLevelPrefix)),
                        "GameManager.RestartLevel");
                }

                var quitGame = AccessTools.Method(gameManagerType, "QuitGame");
                if (quitGame != null)
                {
                    PatchLogger.SafePatchPrefix(harmony, quitGame,
                        new HarmonyMethod(typeof(GameLifecyclePatches), nameof(OnQuitGamePrefix)),
                        "GameManager.QuitGame");
                }

                var pauseGame = AccessTools.Method(gameManagerType, "PauseGame");
                if (pauseGame != null)
                {
                    PatchLogger.SafePatch(harmony, pauseGame,
                        new HarmonyMethod(typeof(GameLifecyclePatches), nameof(OnPauseGamePostfix)),
                        "GameManager.PauseGame");
                }
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply lifecycle patches: {ex.Message}");
            }
        }

        static bool OnReturnToMenuPrefix()
        {
            if (!MultiplayerSession.IsActive) return true;

            PatchLogger.Log("[Lifecycle] ReturnToMenu called during active session - disconnecting all players.");

            if (MultiplayerSession.IsHost && TcpNetworkManager.Instance != null)
            {
                TcpNetworkManager.Instance.SendToAllClients(
                    new Networking.Messages.MsgDisconnect { Reason = "HostReturnedToMenu" });
            }

            TcpNetworkManager.Instance?.Dispose();
            MultiplayerSession.End();
            PatchLogger.Log("[Lifecycle] Session ended due to menu return.");
            return true;
        }

        static bool OnRestartLevelPrefix()
        {
            if (!MultiplayerSession.IsActive) return true;

            if (!MultiplayerSession.IsHost)
            {
                PatchLogger.LogWarning("[Lifecycle] Client attempted RestartLevel - blocked (host authority).");
                return false;
            }

            PatchLogger.Log("[Lifecycle] Host restarting level.");

            if (TcpNetworkManager.Instance != null)
            {
                var syncMsg = new Networking.Messages.MsgSyncState
                {
                    Tick = MultiplayerSession.Tick,
                    PlayerCount = MultiplayerSession.PlayerCount
                };
                TcpNetworkManager.Instance.SendToAllClients(syncMsg);
                PatchLogger.LogDebug("[Lifecycle] Host broadcasted SyncState after level restart.");
            }

            return true;
        }

        static bool OnQuitGamePrefix()
        {
            if (!MultiplayerSession.IsActive) return true;

            PatchLogger.Log("[Lifecycle] QuitGame called during active session - cleaning up session.");

            if (MultiplayerSession.IsHost && TcpNetworkManager.Instance != null)
            {
                TcpNetworkManager.Instance.SendToAllClients(
                    new Networking.Messages.MsgDisconnect { Reason = "HostQuit" });
            }

            TcpNetworkManager.Instance?.Dispose();
            MultiplayerSession.End();
            return true;
        }

        static void OnPauseGamePostfix()
        {
            if (!MultiplayerSession.IsActive) return;
            PatchLogger.Log("[Lifecycle] Game paused.");
        }
    }
}
