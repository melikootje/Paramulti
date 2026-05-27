using System;
using System.Collections.Generic;
using System.Linq;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Session
{
    public static class SaveManager
    {
        const int DefaultChunkSize = 64;
        const float DefaultTimeoutSeconds = 15f;

        static bool _isSaving;
        static bool _isLoading;
        static uint _currentSaveTick;
        static string _currentSceneName;
        static HashSet<int> _saveAcks;
        static int _loadChunksReceived;
        static int _loadTotalChunks;
        static List<EntitySnapshotEntry> _accumulatedEntities;

        public static event Action<bool, string> OnSaveComplete;
        public static event Action<bool, string> OnLoadComplete;

        public static bool IsSaving => _isSaving;
        public static bool IsLoading => _isLoading;

        static SaveManager()
        {
            _saveAcks = new HashSet<int>();
            _accumulatedEntities = new List<EntitySnapshotEntry>();
        }

        public static void InitiateSave(string sceneName, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            if (!MultiplayerSession.IsActive || !MultiplayerSession.IsHost)
            {
                Plugin.Log.LogWarning("[SaveManager] Save can only be initiated by host during active session.");
                return;
            }

            if (_isSaving)
            {
                Plugin.Log.LogWarning("[SaveManager] Save already in progress, ignoring.");
                return;
            }

            _isSaving = true;
            _currentSaveTick = MultiplayerSession.Tick;
            _currentSceneName = sceneName ?? "";
            _saveAcks.Clear();

            var msg = new MsgSaveInitiate
            {
                Tick = _currentSaveTick,
                SceneName = _currentSceneName,
                TimeoutSeconds = timeoutSeconds
            };

            TcpNetworkManager.Instance?.SendToAllClients(msg);
            Plugin.Log.LogInfo($"[SaveManager] Save initiated: tick={_currentSaveTick}, scene={sceneName}");
        }

        public static void HandleSaveAck(MsgSaveAck ack)
        {
            if (!_isSaving) return;

            _saveAcks.Add(ack.PlayerId);

            if (!ack.Success)
            {
                Plugin.Log.LogWarning($"[SaveManager] SaveAck failed from player {ack.PlayerId}: {ack.ErrorMessage}");
            }

            int totalPlayers = MultiplayerSession.PlayerCount;
            Plugin.Log.LogInfo($"[SaveManager] SaveAck from player {ack.PlayerId}. Acks: {_saveAcks.Count}/{totalPlayers}");

            if (_saveAcks.Count >= totalPlayers - 1)
            {
                CompleteSave(true);
            }
        }

        static void CompleteSave(bool success)
        {
            int acks = _saveAcks.Count;
            int total = MultiplayerSession.PlayerCount;

            var completeMsg = new MsgSaveComplete
            {
                Tick = _currentSaveTick,
                AllAcksReceived = acks >= total - 1,
                AcksReceived = acks,
                TotalPlayers = total
            };

            TcpNetworkManager.Instance?.SendToAllClients(completeMsg);

            _isSaving = false;
            string detail = $"Save complete: {acks}/{total} acks";
            Plugin.Log.LogInfo($"[SaveManager] {detail}");
            OnSaveComplete?.Invoke(success, detail);
        }

        public static void HandleSaveComplete(MsgSaveComplete complete)
        {
            if (!MultiplayerSession.IsActive) return;

            bool success = complete.AllAcksReceived;
            string detail = $"Remote save complete: {complete.AcksReceived}/{complete.TotalPlayers} acks";
            Plugin.Log.LogInfo($"[SaveManager] {detail}");
            OnSaveComplete?.Invoke(success, detail);
        }

        public static void InitiateLoad(string sceneName)
        {
            if (!MultiplayerSession.IsActive || !MultiplayerSession.IsHost)
            {
                Plugin.Log.LogWarning("[SaveManager] Load can only be initiated by host during active session.");
                return;
            }

            if (_isLoading)
            {
                Plugin.Log.LogWarning("[SaveManager] Load already in progress, ignoring.");
                return;
            }

            _isLoading = true;
            _currentSceneName = sceneName ?? "";
            _accumulatedEntities.Clear();

            var snapshot = EntitySyncManager.BuildSnapshot();
            int totalChunks = (int)Math.Ceiling((double)snapshot.Entities.Count / DefaultChunkSize);
            if (totalChunks < 1) totalChunks = 1;

            var initMsg = new MsgLoadInitiate
            {
                Tick = MultiplayerSession.Tick,
                SceneName = _currentSceneName,
                TotalChunks = totalChunks
            };

            TcpNetworkManager.Instance?.SendToAllClients(initMsg);

            for (int i = 0; i < totalChunks; i++)
            {
                int start = i * DefaultChunkSize;
                int count = Math.Min(DefaultChunkSize, snapshot.Entities.Count - start);
                var chunk = new MsgLoadStateChunk
                {
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    Entities = snapshot.Entities.GetRange(start, count)
                };
                TcpNetworkManager.Instance?.SendToAllClients(chunk);
            }

            Plugin.Log.LogInfo($"[SaveManager] Load initiated: tick={MultiplayerSession.Tick}, scene={sceneName}, chunks={totalChunks}");
        }

        public static void HandleLoadChunk(MsgLoadStateChunk chunk)
        {
            if (!_isLoading) return;

            _accumulatedEntities.AddRange(chunk.Entities);
            _loadChunksReceived++;

            Plugin.Log.LogInfo($"[SaveManager] LoadChunk received: {chunk.ChunkIndex}/{chunk.TotalChunks}. Total: {_loadChunksReceived}");

            if (_loadChunksReceived >= chunk.TotalChunks)
            {
                ApplyLoadedState();
            }
        }

        static void ApplyLoadedState()
        {
            EntitySyncManager.ClearAll();

            var snapshot = new MsgFullStateSnapshot
            {
                Tick = MultiplayerSession.Tick,
                PlayerCount = MultiplayerSession.PlayerCount
            };
            snapshot.Entities.AddRange(_accumulatedEntities);

            EntitySyncManager.ApplyFullStateSnapshot(snapshot);

            _isLoading = false;
            int received = _loadChunksReceived;
            _loadChunksReceived = 0;
            _accumulatedEntities.Clear();

            string detail = $"Load complete: {received} chunks applied, {snapshot.Entities.Count} entities";
            Plugin.Log.LogInfo($"[SaveManager] {detail}");
            OnLoadComplete?.Invoke(true, detail);
        }

        public static void HandleLoadComplete(MsgLoadComplete complete)
        {
            if (!MultiplayerSession.IsActive || !MultiplayerSession.IsHost) return;

            bool success = complete.Success;
            string detail = $"Client load complete: player={complete.PlayerId}, chunks={complete.ChunksReceived}/{complete.TotalChunks}";
            Plugin.Log.LogInfo($"[SaveManager] {detail}");
        }

        public static void HandleLoadInitiate(MsgLoadInitiate init)
        {
            if (!MultiplayerSession.IsActive || MultiplayerSession.IsHost) return;

            _isLoading = true;
            _loadChunksReceived = 0;
            _loadTotalChunks = init.TotalChunks;
            _accumulatedEntities.Clear();

            Plugin.Log.LogInfo($"[SaveManager] Remote load initiated: tick={init.Tick}, scene={init.SceneName}, chunks={init.TotalChunks}");
        }

        public static void Reset()
        {
            _isSaving = false;
            _isLoading = false;
            _saveAcks.Clear();
            _accumulatedEntities.Clear();
            _loadChunksReceived = 0;
            _loadTotalChunks = 0;
        }
    }
}
