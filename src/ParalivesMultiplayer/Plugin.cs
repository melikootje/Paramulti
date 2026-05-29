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
        static float _lastPingTime;
        const float PingInterval = 2f;
        static ConfigEntry<int> _cfgListenPort;
        static ConfigEntry<string> _cfgConnectAddress;
        static ConfigEntry<bool> _cfgEnablePatches;
        static ConfigEntry<bool> _cfgBuildSyncDryRun;
        static ConfigEntry<bool> _cfgBuildSyncRealApply;
        static ConfigEntry<bool> _cfgEnableLivePlayerSync;

        static ConfigEntry<bool> _cfgEnableReconnect;
        static ConfigEntry<int> _cfgReconnectAttempts;
        static ConfigEntry<int> _cfgReconnectDelayMs;
        static ConfigEntry<int> _cfgSessionTTLSeconds;

        static ConfigEntry<bool> _cfgEnableErrorRecovery;
        static ConfigEntry<int> _cfgCircuitBreakerThreshold;
        static ConfigEntry<int> _cfgCircuitBreakerResetMs;

        static ConfigEntry<bool> _cfgEnableConnectionQuality;
        static ConfigEntry<int> _cfgDegradedThresholdMs;
        static ConfigEntry<int> _cfgCriticalThresholdMs;

        static ConfigEntry<bool> _cfgEnableWatchdog;
        static ConfigEntry<int> _cfgWatchdogIntervalMs;
        static ConfigEntry<int> _cfgStuckThresholdMs;

        static ConfigEntry<bool> _cfgEnableMessageAuth;
        static ConfigEntry<string> _cfgSharedSecret;
        static ConfigEntry<int> _cfgMaxMessagesPerSecond;

        public static bool VerboseLogging => _cfgVerboseLogging?.Value ?? false;
        public static int ListenPort => _cfgListenPort?.Value ?? 7890;
        public static string ConnectAddress => _cfgConnectAddress?.Value ?? "127.0.0.1";
        public static bool EnablePatches => _cfgEnablePatches?.Value ?? true;
        public static bool BuildSyncDryRun => _cfgBuildSyncDryRun?.Value ?? false;
        public static bool BuildSyncRealApply => _cfgBuildSyncRealApply?.Value ?? false;
        public static bool EnableLivePlayerSync { get; set; }
        public static bool EnableLivePlayerSyncConfig => _cfgEnableLivePlayerSync?.Value ?? true;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            try
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
                _cfgEnableLivePlayerSync = Config.Bind("LivePlayerSync", "EnableLivePlayerSync", true,
                    "Enable live player position/rotation synchronization.");

                _cfgEnableReconnect = Config.Bind("Production", "EnableReconnect", true,
                    "Enable automatic reconnection after network interruption.");
                _cfgReconnectAttempts = Config.Bind("Production", "ReconnectAttempts", 5,
                    "Maximum reconnection attempts before giving up.");
                _cfgReconnectDelayMs = Config.Bind("Production", "ReconnectDelayMs", 1000,
                    "Base delay between reconnect attempts (exponential backoff).");
                _cfgSessionTTLSeconds = Config.Bind("Production", "SessionTTLSeconds", 120,
                    "How long to keep session state for rejoining clients (seconds).");

                _cfgEnableErrorRecovery = Config.Bind("Production", "EnableErrorRecovery", true,
                    "Enable error recovery and circuit breaker pattern.");
                _cfgCircuitBreakerThreshold = Config.Bind("Production", "CircuitBreakerThreshold", 50,
                    "Number of errors before opening circuit breaker.");
                _cfgCircuitBreakerResetMs = Config.Bind("Production", "CircuitBreakerResetMs", 30000,
                    "Time to wait before attempting to close circuit breaker (ms).");

                _cfgEnableConnectionQuality = Config.Bind("Production", "EnableConnectionQuality", true,
                    "Enable connection quality monitoring and HUD indicators.");
                _cfgDegradedThresholdMs = Config.Bind("Production", "DegradedThresholdMs", 150,
                    "Ping threshold for degraded connection warning (ms).");
                _cfgCriticalThresholdMs = Config.Bind("Production", "CriticalThresholdMs", 400,
                    "Ping threshold for critical connection status (ms).");

                _cfgEnableWatchdog = Config.Bind("Production", "EnableWatchdog", true,
                    "Enable watchdog timer for stuck network threads.");
                _cfgWatchdogIntervalMs = Config.Bind("Production", "WatchdogIntervalMs", 5000,
                    "How often to check thread health (ms).");
                _cfgStuckThresholdMs = Config.Bind("Production", "StuckThresholdMs", 10000,
                    "Time without heartbeat before marking thread stuck (ms).");

                _cfgEnableMessageAuth = Config.Bind("Security", "EnableMessageAuth", false,
                    "Enable HMAC message authentication for critical messages.");
                _cfgSharedSecret = Config.Bind("Security", "SharedSecret", "",
                    "Shared secret for message authentication (leave empty to disable).");
                _cfgMaxMessagesPerSecond = Config.Bind("Security", "MaxMessagesPerSecond", 100,
                    "Maximum messages per second allowed per client.");

                Log.LogInfo("[Init] Step 1: MessageRegistry");
                MessageRegistry.LogAction = msg => Log.LogInfo(msg);
                MessageRegistry.RegisterAll();
                Log.LogInfo("[Init] Step 2: DesyncDetector");
                DesyncDetector.Initialize();
                Log.LogInfo("[Init] Step 3: InputRouter");
                InputRouter.Initialize();
                Log.LogInfo("[Init] Step 4: BuildSyncManager");
                BuildSyncManager.SetModes(BuildSyncDryRun, BuildSyncRealApply);

                Log.LogInfo("[Init] Step 4b: PlayerSyncManager");
                EnableLivePlayerSync = _cfgEnableLivePlayerSync.Value;
                Session.PlayerSyncManager.Initialize();

                Log.LogInfo("[Init] Step 5: ReconnectionManager");
                ReconnectionManager.Initialize(
                    _cfgEnableReconnect.Value,
                    _cfgReconnectAttempts.Value,
                    _cfgReconnectDelayMs.Value,
                    _cfgSessionTTLSeconds.Value);

                Log.LogInfo("[Init] Step 6: ErrorRecoveryManager");
                ErrorRecoveryManager.Initialize(
                    _cfgEnableErrorRecovery.Value,
                    _cfgCircuitBreakerThreshold.Value,
                    _cfgCircuitBreakerResetMs.Value);

                Log.LogInfo("[Init] Step 7: ConnectionQualityMonitor");
                ConnectionQualityMonitor.Initialize(
                    _cfgEnableConnectionQuality.Value,
                    _cfgDegradedThresholdMs.Value,
                    _cfgCriticalThresholdMs.Value);

                Log.LogInfo("[Init] Step 8: SessionWatchdog");
                SessionWatchdog.Initialize(
                    _cfgEnableWatchdog.Value,
                    _cfgWatchdogIntervalMs.Value,
                    _cfgStuckThresholdMs.Value);

                Log.LogInfo("[Init] Step 9: MessageAuthenticator");
                MessageAuthenticator.Initialize(
                    _cfgEnableMessageAuth.Value,
                    _cfgSharedSecret.Value);

                Log.LogInfo("[Init] Step 10: RateLimiter");
                RateLimiter.Initialize(
                    true,
                    _cfgMaxMessagesPerSecond.Value);

                Log.LogInfo("[Init] Step 11: WireEntitySyncEvents");
                WireEntitySyncEvents();

                Log.LogInfo("[Init] Step 11b: RemoteCharacterManager");
                Session.RemoteCharacterManager.Initialize();
                WireRemoteCharacterEvents();

                Log.LogInfo("[Init] Step 11c: CharacterOwnershipManager");
                Session.CharacterOwnershipManager.Initialize();

                Log.LogInfo("[Init] Step 11d: InteractionSyncManager");
                Session.InteractionSyncManager.Initialize();

                Log.LogInfo("[Init] Step 11e: HouseholdSyncManager");
                Session.HouseholdSyncManager.Initialize();

                Log.LogInfo("[Init] Step 12: Harmony patches");
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

                Log.LogInfo("[Init] Step 13: CommandHandler");
                CommandHandler.Initialize();

                Log.LogInfo($"[{PluginInfo.NAME}] Initialized successfully.");
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[ParalivesMultiplayer] FATAL Awake error: {ex}");
                try { Log?.LogError($"[{PluginInfo.NAME}] FATAL Awake error: {ex}"); } catch {}
            }
        }

        private void Update()
        {
            try
            {
                OnGameUpdate();
            }
            catch (Exception ex)
            {
                Log?.LogError($"[Paramulti] Unhandled exception in Update: {ex}");
            }
        }

        static float _lastLocalCaptureTime;
        const float LocalCaptureInterval = 0.05f;

        static bool _gameSceneLoaded;

        public static void OnGameUpdate()
        {
            MainThreadQueue.Drain();
            TcpNetworkManager.Instance?.ProcessIncomingMessages();
            CommandHandler.ProcessInput();
            MultiplayerHUD.Update();

            if (MultiplayerSession.IsActive)
            {
                if (!_gameSceneLoaded && ParalivesGameApiResolver.CharacterManagerInstance != null)
                {
                    _gameSceneLoaded = true;
                    Log.LogInfo("[Paramulti] Game scene detected (CharacterManager instance available)");
                }

                if (_gameSceneLoaded && !Session.RemoteCharacterManager.HasLocalCharacter)
                    Session.RemoteCharacterManager.FindLocalCharacter();

                if (_gameSceneLoaded)
                    CaptureAndSendLocalState();

                DesyncDetector.TickCheck();
                InputRouter.UpdateDecay();
                Session.PlayerSyncManager.Enabled = EnableLivePlayerSync;
                Session.PlayerSyncManager.Update();
                SendHeartbeatIfNeeded();
                SendPingIfNeeded();
                ReconnectionManager.CleanExpiredStates();
            }
        }

        static void CaptureAndSendLocalState()
        {
            var now = UnityEngine.Time.time;
            if (now - _lastLocalCaptureTime < LocalCaptureInterval) return;
            _lastLocalCaptureTime = now;

            try
            {
                var transform = Session.RemoteCharacterManager.LocalCharacterTransform;
                if (transform == null)
                {
                    Session.RemoteCharacterManager.FindLocalCharacter();
                    transform = Session.RemoteCharacterManager.LocalCharacterTransform;
                    if (transform == null) return;
                }

                var pos = transform.position;
                var rot = transform.rotation;
                var vel = UnityEngine.Vector3.zero;

                var msg = new Networking.Messages.MsgUpdateState
                {
                    Tick = MultiplayerSession.Tick,
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    Position = pos.FromUnity(),
                    Velocity = vel.FromUnity(),
                    Rotation = rot.FromUnity()
                };

                if (MultiplayerSession.IsHost)
                {
                    TcpNetworkManager.Instance?.SendToAllClients(msg);
                    Log.LogDebug($"[Paramulti][Local] OnGameUpdate host state capture. pos={pos}");
                }
                else if (TcpNetworkManager.Instance != null)
                {
                    TcpNetworkManager.Instance.SendToHost(msg);
                    Log.LogDebug($"[Paramulti][Local] OnGameUpdate client state capture. pos={pos}");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[Paramulti] Local state capture error in OnGameUpdate: {ex.Message}");
            }
        }

        private void OnGUI()
        {
            MultiplayerHUD.Draw();
        }

        private void OnDestroy()
        {
            try { SessionWatchdog.Dispose(); } catch {}
            try { TcpNetworkManager.Instance?.Dispose(); } catch {}
            try { Session.RemoteCharacterManager.OnSessionEnd(); } catch {}
            try { MultiplayerSession.End(); } catch {}
            try { Log?.LogInfo($"[{PluginInfo.NAME}] Shut down."); } catch {}
        }

        static void ApplyPatches(Harmony harmony)
        {
            Log.LogInfo("[Patch] Applying Harmony patches...");

            SceneManagementPatches.Apply(harmony);
            GameLifecyclePatches.Apply(harmony);
            PlayerStatePatches.Apply(harmony);
            BuildModePatches.Apply(harmony);
            GameLoopPatches.Apply(harmony);

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
                // Don't register the local player with DesyncDetector - it doesn't send heartbeats to itself
                if (id != MultiplayerSession.LocalPlayerId)
                    DesyncDetector.RegisterPlayer(id);
                InputRouter.RegisterRemotePlayer(id);
                BuildSyncManager.RegisterPlayer(id);
                Session.PlayerSyncManager.RegisterPlayer(id);
                ConnectionQualityMonitor.RegisterClient(id);
                RateLimiter.RegisterClient(id);
            };

            MultiplayerSession.PlayerLeft += (id) =>
            {
                if (id != MultiplayerSession.LocalPlayerId)
                    DesyncDetector.UnregisterPlayer(id);
                InputRouter.UnregisterRemotePlayer(id);
                BuildSyncManager.UnregisterPlayer(id);
                Session.PlayerSyncManager.UnregisterPlayer(id);
                ConnectionQualityMonitor.UnregisterClient(id);
                RateLimiter.UnregisterClient(id);
            };

            BuildSyncManager.OnBuildEventRejected += (id, reason) =>
            {
                Log.LogWarning($"[BuildSync] Rejected build event from player {id}: {reason}");
            };

            BuildSyncManager.OnBuildEventApplied += (evt) =>
            {
                Log.LogInfo($"[BuildSync] Applied build event: type={evt.EventType}, entity={evt.EntityId}, player={evt.PlayerId}");
            };

            ErrorRecoveryManager.OnCircuitStateChanged += (isOpen) =>
            {
                if (isOpen)
                    Log.LogWarning("[ErrorRecovery] Circuit breaker OPENED — pausing sync");
                else
                    Log.LogInfo("[ErrorRecovery] Circuit breaker CLOSED — resuming sync");
            };

            ErrorRecoveryManager.OnPlayerErrorThreshold += (id, reason) =>
            {
                Log.LogWarning($"[ErrorRecovery] Player {id} error threshold: {reason}");
            };

            ReconnectionManager.OnReconnectSucceeded += (id, msg) =>
            {
                Log.LogInfo($"[Reconnect] Success for player {id}: {msg}");
            };

            ReconnectionManager.OnReconnectFailed += (id, reason) =>
            {
                Log.LogWarning($"[Reconnect] Failed for player {id}: {reason}");
            };

            SessionWatchdog.OnThreadStuckDetected += (name) =>
            {
                Log.LogError($"[Watchdog] Thread stuck: {name}");
            };

             Log.LogInfo("[Init] Phase 10 production hardening events wired.");
        }

        static void WireRemoteCharacterEvents()
        {
            Session.PlayerSyncManager.OnRemotePlayerRender += (playerId, position, rotation) =>
            {
                Session.RemoteCharacterManager.ApplyRemoteState(playerId, position, rotation);
            };

            Session.RemoteCharacterManager.OnRemoteCharacterCreated += (playerId, entry) =>
            {
                Session.PlayerSyncManager.SetProxyRouted(playerId, true);
                Log.LogInfo($"[Paramulti] Proxy routed for player {playerId}");
            };

            Session.RemoteCharacterManager.OnRemoteCharacterRemoved += (playerId) =>
            {
                Session.PlayerSyncManager.SetProxyRouted(playerId, false);
            };

            MultiplayerSession.PlayerJoined += (id, name) =>
            {
                if (id == MultiplayerSession.LocalPlayerId)
                {
                    // Register our own character ownership
                    Session.RemoteCharacterManager.RegisterLocalCharacterOwnership();
                }
                else
                {
                    // For remote players, character creation is deferred until we receive their MsgCharacterDataSync
                    Log.LogInfo($"[Paramulti] Player {name} (id={id}) joined; awaiting character data sync...");
                }
            };

            MultiplayerSession.PlayerLeft += (id) =>
            {
                Session.RemoteCharacterManager.RemoveRemoteCharacter(id);
                Session.CharacterOwnershipManager.UnregisterOwnership(id);
            };

            MultiplayerSession.OnSessionEnded += () =>
            {
                _gameSceneLoaded = false;
                Session.RemoteCharacterManager.OnSessionEnd();
                Session.CharacterOwnershipManager.ClearAll();
                Session.HouseholdSyncManager.ClearAll();
                Session.InteractionSyncManager.ClearAll();
            };

            Session.InteractionSyncManager.OnRemoteInteractionRequested += (requesterPlayerId, targetGuid, interactionGuid) =>
            {
                Log.LogInfo($"[Paramulti] Remote interaction requested by player {requesterPlayerId} on character {targetGuid:X}, interaction={interactionGuid:X}");
            };

            Log.LogInfo("[Init] RemoteCharacter events wired.");
        }

        static void SendHeartbeatIfNeeded()
        {
            var now = UnityEngine.Time.time;
            if (now - _lastHeartbeatTime < HeartbeatInterval) return;
            _lastHeartbeatTime = now;

            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            DesyncDetector.BuildHeartbeat(out var hb);
            Log.LogInfo($"[HeartbeatDebug] Sending heartbeat: playerId={hb.PlayerId}, tick={hb.Tick}, seq={hb.SequenceNumber}, ts={hb.TimestampMs}, isHost={net.IsHost}");
            if (net.IsHost)
                net.SendToAllClients(hb);
            else
                net.SendToHost(hb);
        }

        static void SendPingIfNeeded()
        {
            var now = UnityEngine.Time.time;
            if (now - _lastPingTime < PingInterval) return;
            _lastPingTime = now;

            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            var ping = new ParalivesMultiplayer.Networking.Messages.MsgPing
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                TimestampMs = System.Diagnostics.Stopwatch.GetTimestamp()
            };

            if (net.IsHost)
                net.SendToAllClients(ping);
            else
                net.SendToHost(ping);
        }
    }

    static class PluginInfo
    {
        public const string GUID = "com.paralives.multiplayer";
        public const string NAME = "ParalivesMultiplayer";
        public const string VERSION = "0.1.0";
    }
}
