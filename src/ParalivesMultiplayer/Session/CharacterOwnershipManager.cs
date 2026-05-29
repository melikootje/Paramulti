using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ParalivesMultiplayer.Session
{
    public static class CharacterOwnershipManager
    {
        public static void Initialize()
        {
            Plugin.Log.LogInfo("[Ownership] Initialized");
        }

        static readonly Dictionary<ulong, int> _guidToPlayer = new Dictionary<ulong, int>();
        static readonly Dictionary<int, ulong> _playerToGuid = new Dictionary<int, ulong>();
        static readonly object _lock = new object();

        public static event Action<int, ulong> OnOwnershipRegistered;
        public static event Action<int, ulong> OnOwnershipRemoved;

        public static void RegisterOwnership(int playerId, ulong characterGuid)
        {
            lock (_lock)
            {
                _guidToPlayer[characterGuid] = playerId;
                _playerToGuid[playerId] = characterGuid;
            }
            Plugin.Log.LogInfo($"[Ownership] Player {playerId} owns character GUID={characterGuid:X}");
            OnOwnershipRegistered?.Invoke(playerId, characterGuid);
        }

        public static void UnregisterOwnership(int playerId)
        {
            ulong guid;
            lock (_lock)
            {
                if (!_playerToGuid.TryGetValue(playerId, out guid)) return;
                _playerToGuid.Remove(playerId);
                _guidToPlayer.Remove(guid);
            }
            Plugin.Log.LogInfo($"[Ownership] Player {playerId} unregistered from character GUID={guid:X}");
            OnOwnershipRemoved?.Invoke(playerId, guid);
        }

        public static void UnregisterByGuid(ulong characterGuid)
        {
            int playerId;
            lock (_lock)
            {
                if (!_guidToPlayer.TryGetValue(characterGuid, out playerId)) return;
                _guidToPlayer.Remove(characterGuid);
                _playerToGuid.Remove(playerId);
            }
            Plugin.Log.LogInfo($"[Ownership] Character GUID={characterGuid:X} unregistered from player {playerId}");
            OnOwnershipRemoved?.Invoke(playerId, characterGuid);
        }

        public static bool TryGetPlayerId(ulong characterGuid, out int playerId)
        {
            lock (_lock) return _guidToPlayer.TryGetValue(characterGuid, out playerId);
        }

        public static bool TryGetCharacterGuid(int playerId, out ulong characterGuid)
        {
            lock (_lock) return _playerToGuid.TryGetValue(playerId, out characterGuid);
        }

        public static bool IsOwnedByLocalPlayer(ulong characterGuid)
        {
            lock (_lock)
            {
                if (_guidToPlayer.TryGetValue(characterGuid, out var pid))
                    return pid == MultiplayerSession.LocalPlayerId;
                return false;
            }
        }

        public static bool IsOwnedByRemotePlayer(ulong characterGuid)
        {
            lock (_lock)
            {
                if (_guidToPlayer.TryGetValue(characterGuid, out var pid))
                    return pid != MultiplayerSession.LocalPlayerId;
                return false;
            }
        }

        public static int[] GetAllPlayerIds()
        {
            lock (_lock) return _playerToGuid.Keys.ToArray();
        }

        public static ulong[] GetAllCharacterGuids()
        {
            lock (_lock) return _guidToPlayer.Keys.ToArray();
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _guidToPlayer.Clear();
                _playerToGuid.Clear();
            }
            Plugin.Log.LogInfo("[Ownership] All ownerships cleared");
        }

        public static ulong GetLocalCharacterGuid()
        {
            lock (_lock)
            {
                if (_playerToGuid.TryGetValue(MultiplayerSession.LocalPlayerId, out var guid))
                    return guid;
                return 0;
            }
        }
    }
}
