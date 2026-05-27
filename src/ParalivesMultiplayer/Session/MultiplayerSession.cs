using System;
using System.Collections.Generic;

namespace ParalivesMultiplayer.Session
{
    public static class MultiplayerSession
    {
        public static bool IsActive { get; private set; }
        public static bool IsHost { get; private set; }
        public static bool IsClient => IsActive && !IsHost;

        public static uint Tick { get; private set; }

        static readonly Dictionary<int, string> _players = new Dictionary<int, string>();
        static readonly object _playersLock = new object();

        public static event Action OnSessionStarted;
        public static event Action OnSessionEnded;
        public static event Action<int, string> PlayerJoined;
        public static event Action<int> PlayerLeft;

        public static void StartAsHost()
        {
            IsActive = true;
            IsHost = true;
            Tick = 0;
            _players.Clear();

            Plugin.Log.LogInfo("[Session] Started as HOST");
            OnSessionStarted?.Invoke();
        }

        public static void StartAsClient()
        {
            IsActive = true;
            IsHost = false;
            Tick = 0;
            _players.Clear();

            Plugin.Log.LogInfo("[Session] Started as CLIENT");
            OnSessionStarted?.Invoke();
        }

        public static void End()
        {
            if (!IsActive) return;
            IsActive = false;
            IsHost = false;
            Tick = 0;
            lock (_playersLock) _players.Clear();

            Plugin.Log.LogInfo("[Session] Ended");
            OnSessionEnded?.Invoke();
        }

        public static void IncrementTick()
        {
            Tick++;
        }

        public static void OnClientConnected(int clientId, string clientName)
        {
            if (!IsActive) return;
            Plugin.Log.LogInfo($"[Session] Client connected: {clientName} (id={clientId})");
        }

        public static void OnPlayerJoined(int playerId, string playerName)
        {
            lock (_playersLock)
            {
                _players[playerId] = playerName;
            }
            Plugin.Log.LogInfo($"[Session] Player joined: {playerName} (id={playerId})");
            PlayerJoined?.Invoke(playerId, playerName);
        }

        public static void OnPlayerLeft(int playerId)
        {
            lock (_playersLock)
            {
                _players.Remove(playerId);
            }
            Plugin.Log.LogInfo($"[Session] Player left: id={playerId}");
            PlayerLeft?.Invoke(playerId);
        }

        public static int PlayerCount
        {
            get
            {
                lock (_playersLock) return _players.Count;
            }
        }

        public static bool TryGetPlayerName(int playerId, out string name)
        {
            lock (_playersLock) return _players.TryGetValue(playerId, out name);
        }

        public static int[] GetPlayerIds()
        {
            lock (_playersLock)
            {
                var ids = new int[_players.Count];
                var i = 0;
                foreach (var kv in _players)
                    ids[i++] = kv.Key;
                return ids;
            }
        }
    }
}
