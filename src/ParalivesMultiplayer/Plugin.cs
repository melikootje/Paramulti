using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ParalivesMultiplayer.Input;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Patches;
using ParalivesMultiplayer.Performance;
using ParalivesMultiplayer.Session;
using ParalivesMultiplayer.UI;

namespace ParalivesMultiplayer
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
    [BepInProcess("Paralives.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        static ConfigEntry<bool> _cfgVerboseLogging;
        static float _lastHeartbeatTime;
        const float HeartbeatInterval = 0.5f;
        static ConfigEntry<int> _cfgListenPort;
        static ConfigEntry<string> _cfgConnectAddress;
        static ConfigEntry<bool> _cfgEnablePatches;
        static ConfigEntry<bool> _cfgBuildSyncDryRun;
        static ConfigEntry<bool> _cfgBuildSyncRealApply;

        public static bool VerboseLogging => _cfgVerboseLogging?.Value ?? false;
        public static int ListenPort => _cfgListenPort?.Value ?? 7890;
        public static string ConnectAddress => _cfgConnectAddress?.Value ?? "127.0.0.1";
        public static bool EnablePatches => _cfgEnablePatches?.Value ?? true;
        public static bool BuildSyncDryRun => _cfgBuildSyncDryRun?.Value ?? false;
        public static bool BuildSyncRealApply => _cfgBuildSyncRealApply?.Value ?? false;

        new void Awake()
        {
            Instance = this;
            Log = Logger;

            Log.LogInfo($"[{PluginInfo.NAME}] v{PluginInfo.VERSION} loading...");

            _cfgVerboseLogging = Config.Bind("Debug", "VerboseLogging", false,
                "Enable verbose network and patch logging.");
            _cfgListenPort = Config.Bind("Network", "ListenPort", 7890,
                "TCP port for host to listen on.");
            _cfgConnectAddress = Config.Bind("Network", "ConnectAddress", "127.0.0.1",
                "Default address for clients to connect to.");
            _cfgEnablePatches = Config.Bind("Harmony", "EnablePatches", true,
                "Enable Harmony patches for game integration.");
            _cfgBuildSyncDryRun = Config.Bind("BuildSync", "DryRunMode", false,
                "Log build events without applying them to game state.");
            _cfgBuildSyncRealApply = Config.Bind("BuildSync", "RealApplyMode", false,
                "Apply remote build events to local entity state (enable for live sync).");

            MessageRegistry.RegisterAll();
            DesyncDetector.Initialize();
            InputRouter.Initialize();
            BuildSyncManager.SetModes(BuildSyncDryRun, BuildSyncRealApply);
            WireEntitySyncEvents();

            if (EnablePatches)
            {
                try
                {
                    var harmony = new Harmony(PluginInfo.GUID);
                    ApplyPatches(harmony);
                }
                catch (Exception ex)
                {
                    Log.LogError($"[{PluginInfo.NAME}] Failed to initialize Harmony: {ex.Message}");
                }
            }
            else
            {
                Log.LogInfo($"[{PluginInfo.NAME}] Harmony patches disabled by config.");
            }

            CommandHandler.Initialize();

            Log.LogInfo($"[{PluginInfo.NAME}] Initialized successfully.");
        }

        new void Update()
        {
            MainThreadQueue.Drain();
            TcpNetworkManager.Instance?.ProcessIncomingMessages();
            CommandHandler.ProcessInput();
            MultiplayerHUD.Update();

            if (MultiplayerSession.IsActive)
            {
                DesyncDetector.TickCheck();
                InputRouter.UpdateDecay();
                SendHeartbeatIfNeeded();
            }
        }

        new void OnGUI()
        {
            MultiplayerHUD.Draw();
        }

        new void OnDestroy()
        {
            TcpNetworkManager.Instance?.Dispose();
            MultiplayerSession.End();
            Log.LogInfo($"[{PluginInfo.NAME}] Shut down.");
        }

        static void ApplyPatches(Harmony harmony)
        {
            Log.LogInfo("[Patch] Applying Harmony patches...");

            SceneManagementPatches.Apply(harmony);
            GameLifecyclePatches.Apply(harmony);
            PlayerStatePatches.Apply(harmony);
            BuildModePatches.Apply(harmony);

            Log.LogInfo("[Patch] All patch containers applied.");
        }

        static void WireEntitySyncEvents()
        {
            EntitySyncManager.OnEntitySpawned += (msg) =>
            {
                var net = TcpNetworkManager.Instance;
                if (net == null) return;
                if (MultiplayerSession.IsHost)
                {
                    Log.LogInfo($"[EntitySync] Host broadcasting spawn: id={msg.EntityId}");
                    net.SendToAllClients(msg);
                }
                else
                {
                    net.SendToHost(msg);
                }
            };

            EntitySyncManager.OnEntityDespawned += (msg) =>
            {
                var net = TcpNetworkManager.Instance;
                if (net == null) return;
                if (MultiplayerSession.IsHost)
                {
                    Log.LogInfo($"[EntitySync] Host broadcasting despawn: id={msg.EntityId}");
                    net.SendToAllClients(msg);
                }
                else
                {
                    net.SendToHost(msg);
                }
            };

            MultiplayerSession.PlayerJoined += (id, name) =>
            {
                LobbyManager.PlayerJoined(id);
                Log.LogInfo($"[Lobby] Player {name} (id={id}) added to lobby");
            };

            MultiplayerSession.PlayerLeft += (id) =>
            {
                LobbyManager.PlayerLeft(id);
                Log.LogInfo($"[Lobby] Player id={id} removed from lobby");
            };

            Log.LogInfo("[Init] EntitySync and Lobby events wired.");

            DesyncDetector.OnDesyncDetected += (id, reason) =>
            {
                Log.LogWarning($"[Desync] Player {id}: {reason}");
            };

            InputRouter.OnRemoteInputReceived += (id, cmd) =>
            {
                Log.LogDebug($"[InputRouter] Remote input from player {id}: action={cmd.Action}");
            };

            MultiplayerSession.PlayerJoined += (id, name) =>
            {
                DesyncDetector.RegisterPlayer(id);
                InputRouter.RegisterRemotePlayer(id);
                BuildSyncManager.RegisterPlayer(id);
            };

            MultiplayerSession.PlayerLeft += (id) =>
            {
                DesyncDetector.UnregisterPlayer(id);
                InputRouter.UnregisterRemotePlayer(id);
                BuildSyncManager.UnregisterPlayer(id);
            };

            BuildSyncManager.OnBuildEventRejected += (id, reason) =>
            {
                Log.LogWarning($"[BuildSync] Rejected build event from player {id}: {reason}");
            };

            BuildSyncManager.OnBuildEventApplied += (evt) =>
            {
                Log.LogInfo($"[BuildSync] Applied build event: type={evt.EventType}, entity={evt.EntityId}, player={evt.PlayerId}");
            };
        }

        static void SendHeartbeatIfNeeded()
        {
            var now = UnityEngine.Time.time;
            if (now - _lastHeartbeatTime < HeartbeatInterval) return;
            _lastHeartbeatTime = now;

            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            DesyncDetector.BuildHeartbeat(out var hb);
            if (net.IsHost)
                net.SendToAllClients(hb);
            else
                net.SendToHost(hb);
        }
    }

    static class PluginInfo
    {
        public const string GUID = "com.paralives.multiplayer";
        public const string NAME = "ParalivesMultiplayer";
        public const string VERSION = "0.1.0";
    }
}
