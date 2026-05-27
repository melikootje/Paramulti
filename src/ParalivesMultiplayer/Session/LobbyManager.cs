using System;
using System.Collections.Generic;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Session
{
    public static class LobbyManager
    {
        static readonly Dictionary<int, bool> _playerReady = new Dictionary<int, bool>();
        static readonly object _lock = new object();

        public static event Action<bool> OnAllPlayersReady;
        public static event Action<int, bool> OnPlayerReadyChanged;

        public static int ReadyCount
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    foreach (var kv in _playerReady)
                        if (kv.Value) count++;
                    return count;
                }
            }
        }

        public static int TotalCount
        {
            get
            {
                lock (_lock) return _playerReady.Count;
            }
        }

        public static void PlayerJoined(int playerId)
        {
            lock (_lock)
            {
                _playerReady[playerId] = false;
            }
            Plugin.Log.LogInfo($"[Lobby] Player {playerId} joined lobby (not ready)");
        }

        public static void PlayerLeft(int playerId)
        {
            lock (_lock) _playerReady.Remove(playerId);
            Plugin.Log.LogInfo($"[Lobby] Player {playerId} left lobby");
            CheckAllReady();
        }

        public static void SetReady(int playerId, bool ready)
        {
            lock (_lock)
            {
                if (!_playerReady.ContainsKey(playerId))
                    _playerReady[playerId] = false;
                _playerReady[playerId] = ready;
            }

            Plugin.Log.LogInfo($"[Lobby] Player {playerId} is {(ready ? "READY" : "NOT READY")} ({ReadyCount}/{TotalCount})");
            OnPlayerReadyChanged?.Invoke(playerId, ready);
            CheckAllReady();
        }

        public static bool IsPlayerReady(int playerId)
        {
            lock (_lock) return _playerReady.TryGetValue(playerId, out var r) && r;
        }

        public static void HandleRemoteReady(MsgReadyCheck msg)
        {
            SetReady(msg.PlayerId, msg.IsReady);
            if (MultiplayerSession.IsHost && TcpNetworkManager.Instance != null)
                TcpNetworkManager.Instance.SendToAllExcept(msg.SenderClientId, msg);
        }

        static void CheckAllReady()
        {
            lock (_lock)
            {
                if (_playerReady.Count == 0) return;
                bool allReady = true;
                foreach (var kv in _playerReady)
                    if (!kv.Value) { allReady = false; break; }

                if (allReady && _playerReady.Count > 0)
                    OnAllPlayersReady?.Invoke(true);
            }
        }

        public static void Reset()
        {
            lock (_lock) _playerReady.Clear();
            Plugin.Log.LogInfo("[Lobby] Lobby reset");
        }
    }
}
