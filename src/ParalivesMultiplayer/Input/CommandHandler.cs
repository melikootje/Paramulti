using System;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Input
{
    public static class CommandHandler
    {
        static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Plugin.Log.LogInfo("[Cmd] Command handler initialized. Press F5=Host, F6=Client, F7=Disconnect.");
        }

        public static void ProcessInput()
        {
            if (!_initialized) return;

            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
                StartHost();
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
                StartClient();
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
                Disconnect();
        }

        static void StartHost()
        {
            if (MultiplayerSession.IsActive)
            {
                Plugin.Log.LogWarning("[Cmd] Session already active. Disconnect first.");
                return;
            }

            try
            {
                TcpNetworkManager.Instance?.Dispose();
                var mgr = new TcpNetworkManager();
                MultiplayerSession.StartAsHost();
                mgr.StartHost(Plugin.ListenPort);
                Plugin.Log.LogInfo($"[Cmd] Started HOST on port {Plugin.ListenPort}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Cmd] Failed to start host: {ex.Message}");
            }
        }

        static void StartClient()
        {
            if (MultiplayerSession.IsActive)
            {
                Plugin.Log.LogWarning("[Cmd] Session already active. Disconnect first.");
                return;
            }

             try
            {
                TcpNetworkManager.Instance?.Dispose();
                var mgr = new TcpNetworkManager();
                MultiplayerSession.StartAsClient();
                mgr.StartClient(Plugin.ConnectAddress, Plugin.ListenPort);
                Plugin.Log.LogInfo($"[Cmd] Connecting as CLIENT to {Plugin.ConnectAddress}:{Plugin.ListenPort}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Cmd] Failed to start client: {ex.Message}");
            }
        }

        static void Disconnect()
        {
            if (!MultiplayerSession.IsActive)
            {
                Plugin.Log.LogWarning("[Cmd] No active session.");
                return;
            }

            try
            {
                TcpNetworkManager.Instance?.Dispose();
                MultiplayerSession.End();
                Plugin.Log.LogInfo("[Cmd] Disconnected.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Cmd] Disconnect error: {ex.Message}");
            }
        }
    }
}
