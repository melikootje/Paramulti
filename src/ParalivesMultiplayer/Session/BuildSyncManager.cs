using System;
using System.Collections.Generic;
using System.Threading;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class BuildSyncManager
    {
        static readonly Dictionary<int, PlayerBuildState> _playerStates = new Dictionary<int, PlayerBuildState>();
        static readonly Dictionary<uint, RemoteBuildObject> _remoteObjects = new Dictionary<uint, RemoteBuildObject>();
        static readonly object _lock = new object();
        static uint _nextRemoteId = 1000;

        public static bool DryRunMode { get; set; }
        public static bool RealApplyMode { get; set; }
        public static bool Enabled => DryRunMode || RealApplyMode;

        public static event Action<int, string> OnBuildEventRejected;
        public static event Action<MsgBuildModeEvent> OnBuildEventApplied;

        const uint MaxTickDrift = 20;
        const uint MaxSequenceGap = 10;

        public static void Initialize()
        {
            DryRunMode = false;
            RealApplyMode = false;
            lock (_lock)
            {
                _playerStates.Clear();
                _remoteObjects.Clear();
            }
            Plugin.Log.LogInfo("[BuildSync] BuildSyncManager initialized (dry-run=false, real-apply=false)");
        }

        public static void SetModes(bool dryRun, bool realApply)
        {
            DryRunMode = dryRun;
            RealApplyMode = realApply;
            Plugin.Log.LogInfo($"[BuildSync] Modes updated: dry-run={dryRun}, real-apply={realApply}");
        }

        public static void RegisterPlayer(int playerId)
        {
            lock (_lock)
            {
                _playerStates[playerId] = new PlayerBuildState
                {
                    PlayerId = playerId,
                    LastSequence = 0,
                    LastTick = 0,
                    EventCount = 0
                };
            }
            Plugin.Log.LogDebug($"[BuildSync] Registered player {playerId} for build sync");
        }

        public static void UnregisterPlayer(int playerId)
        {
            lock (_lock)
            {
                _playerStates.Remove(playerId);
            }
            Plugin.Log.LogDebug($"[BuildSync] Unregistered player {playerId} from build sync");
        }

        public static bool ValidateAndApply(MsgBuildModeEvent evt)
        {
            if (!Enabled)
            {
                Plugin.Log.LogDebug("[BuildSync] Build sync disabled, ignoring event");
                return false;
            }

            lock (_lock)
            {
                if (!_playerStates.TryGetValue(evt.PlayerId, out var state))
                {
                    Plugin.Log.LogWarning($"[BuildSync] Unknown player {evt.PlayerId} in build event, rejecting");
                    OnBuildEventRejected?.Invoke(evt.PlayerId, "Unknown player");
                    return false;
                }

                uint tickDrift = (uint)Math.Abs((long)evt.Tick - (long)MultiplayerSession.Tick);
                if (tickDrift > MaxTickDrift)
                {
                    Plugin.Log.LogWarning($"[BuildSync] Tick drift {tickDrift} for player {evt.PlayerId}, rejecting event");
                    OnBuildEventRejected?.Invoke(evt.PlayerId, $"Tick drift: {tickDrift}");
                    return false;
                }

                if (evt.EntityId > 0 && evt.EventType == BuildEventType.ObjectPlaced)
                {
                    if (evt.EntityId < state.LastSequence && state.LastSequence > 0)
                    {
                        uint gap = state.LastSequence - evt.EntityId;
                        if (gap > MaxSequenceGap)
                        {
                            Plugin.Log.LogWarning($"[BuildSync] Sequence gap {gap} for player {evt.PlayerId}, rejecting event");
                            OnBuildEventRejected?.Invoke(evt.PlayerId, $"Sequence gap: {gap}");
                            return false;
                        }
                    }

                    state.LastSequence = evt.EntityId > state.LastSequence ? evt.EntityId : state.LastSequence;
                }

                state.LastTick = evt.Tick;
                state.EventCount++;
            }

            if (DryRunMode)
            {
                Plugin.Log.LogInfo($"[BuildSync] [DRY-RUN] Would apply: type={evt.EventType}, entity={evt.EntityId}, pos={evt.Position}, player={evt.PlayerId}");
                return true;
            }

            if (!RealApplyMode)
            {
                Plugin.Log.LogDebug("[BuildSync] Neither dry-run nor real-apply enabled, ignoring event");
                return false;
            }

            try
            {
                ApplyBuildEventInternal(evt);
                OnBuildEventApplied?.Invoke(evt);
                Plugin.Log.LogInfo($"[BuildSync] Applied: type={evt.EventType}, entity={evt.EntityId}, player={evt.PlayerId}");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[BuildSync] Failed to apply build event: {ex.Message}");
                RollbackBuildEvent(evt);
                OnBuildEventRejected?.Invoke(evt.PlayerId, $"Apply failed: {ex.Message}");
                return false;
            }
        }

        static void ApplyBuildEventInternal(MsgBuildModeEvent evt)
        {
            switch (evt.EventType)
            {
                case BuildEventType.ObjectPlaced:
                    ApplyObjectPlaced(evt);
                    break;
                case BuildEventType.ObjectRemoved:
                    ApplyObjectRemoved(evt);
                    break;
                case BuildEventType.ObjectMoved:
                    ApplyObjectMoved(evt);
                    break;
                case BuildEventType.ObjectRotated:
                    ApplyObjectRotated(evt);
                    break;
                case BuildEventType.ObjectScaled:
                    ApplyObjectScaled(evt);
                    break;
                case BuildEventType.ObjectStyled:
                    ApplyObjectStyled(evt);
                    break;
                case BuildEventType.ModeEntered:
                case BuildEventType.ModeExited:
                    Plugin.Log.LogDebug($"[BuildSync] Mode event: {evt.EventType} from player {evt.PlayerId}");
                    break;
                default:
                    Plugin.Log.LogDebug($"[BuildSync] Ignoring unsupported event type: {evt.EventType}");
                    break;
            }
        }

        static void ApplyObjectPlaced(MsgBuildModeEvent evt)
        {
            uint id = evt.EntityId;
            if (id == 0)
            {
                lock (_lock) id = _nextRemoteId++;
            }

            var obj = new RemoteBuildObject
            {
                EntityId = id,
                ObjectTypeId = evt.ObjectTypeId,
                Position = evt.Position,
                Rotation = evt.Rotation,
                Scale = evt.Scale,
                StyleName = evt.StyleName,
                OwnerPlayerId = evt.PlayerId,
                CreatedTick = evt.Tick
            };

            lock (_lock) _remoteObjects[id] = obj;

            EntitySyncManager.RegisterSpawn(
                id,
                evt.ObjectTypeId,
                evt.Position,
                evt.Rotation,
                evt.Scale,
                evt.PlayerId);
        }

        static void ApplyObjectRemoved(MsgBuildModeEvent evt)
        {
            lock (_lock) _remoteObjects.Remove(evt.EntityId);
            EntitySyncManager.RegisterDespawn(evt.EntityId, evt.PlayerId);
        }

        static void ApplyObjectMoved(MsgBuildModeEvent evt)
        {
            lock (_lock)
            {
                if (_remoteObjects.TryGetValue(evt.EntityId, out var obj))
                {
                    obj.Position = evt.Position;
                    _remoteObjects[evt.EntityId] = obj;
                }
            }

            if (EntitySyncManager.TryGetEntity(evt.EntityId, out var record))
            {
                record.Position = evt.Position;
            }
        }

        static void ApplyObjectRotated(MsgBuildModeEvent evt)
        {
            lock (_lock)
            {
                if (_remoteObjects.TryGetValue(evt.EntityId, out var obj))
                {
                    obj.Rotation = evt.Rotation;
                    _remoteObjects[evt.EntityId] = obj;
                }
            }

            if (EntitySyncManager.TryGetEntity(evt.EntityId, out var record))
            {
                record.Rotation = evt.Rotation;
            }
        }

        static void ApplyObjectScaled(MsgBuildModeEvent evt)
        {
            lock (_lock)
            {
                if (_remoteObjects.TryGetValue(evt.EntityId, out var obj))
                {
                    obj.Scale = evt.Scale;
                    _remoteObjects[evt.EntityId] = obj;
                }
            }

            if (EntitySyncManager.TryGetEntity(evt.EntityId, out var record))
            {
                record.Scale = evt.Scale;
            }
        }

        static void ApplyObjectStyled(MsgBuildModeEvent evt)
        {
            lock (_lock)
            {
                if (_remoteObjects.TryGetValue(evt.EntityId, out var obj))
                {
                    obj.StyleName = evt.StyleName;
                    _remoteObjects[evt.EntityId] = obj;
                }
            }
        }

        static void RollbackBuildEvent(MsgBuildModeEvent evt)
        {
            switch (evt.EventType)
            {
                case BuildEventType.ObjectPlaced:
                    lock (_lock) _remoteObjects.Remove(evt.EntityId);
                    EntitySyncManager.RegisterDespawn(evt.EntityId, evt.PlayerId);
                    Plugin.Log.LogWarning($"[BuildSync] Rolled back placed object {evt.EntityId}");
                    break;
                case BuildEventType.ObjectRemoved:
                    if (EntitySyncManager.TryGetEntity(evt.EntityId, out var rec))
                    {
                        EntitySyncManager.RegisterSpawn(
                            rec.EntityId, rec.EntityType, rec.Position, rec.Rotation, rec.Scale, rec.OwnerPlayerId);
                    }
                    Plugin.Log.LogWarning($"[BuildSync] Rolled back removed object {evt.EntityId}");
                    break;
                default:
                    Plugin.Log.LogDebug($"[BuildSync] No rollback needed for event type {evt.EventType}");
                    break;
            }
        }

        public static int RemoteObjectCount
        {
            get
            {
                lock (_lock) return _remoteObjects.Count;
            }
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _playerStates.Clear();
                _remoteObjects.Clear();
            }
            Plugin.Log.LogInfo("[BuildSync] Cleared all build sync state");
        }
    }

    class PlayerBuildState
    {
        public int PlayerId;
        public uint LastSequence;
        public uint LastTick;
        public int EventCount;
    }

    class RemoteBuildObject
    {
        public uint EntityId;
        public string ObjectTypeId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public string StyleName;
        public int OwnerPlayerId;
        public uint CreatedTick;
    }
}
