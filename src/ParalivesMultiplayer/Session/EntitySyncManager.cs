using System;
using System.Collections.Generic;
using System.Threading;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class EntitySyncManager
    {
        static readonly Dictionary<uint, EntityRecord> _entities = new Dictionary<uint, EntityRecord>();
        static readonly object _lock = new object();
        static uint _nextEntityId = 1;

        public static event Action<MsgEntitySpawn> OnEntitySpawned;
        public static event Action<MsgEntityDespawn> OnEntityDespawned;

        public static int EntityCount
        {
            get
            {
                lock (_lock) return _entities.Count;
            }
        }

        public static void RegisterSpawn(uint entityId, string entityType, Vector3 position, Quaternion rotation, Vector3 scale, int playerId)
        {
            lock (_lock)
            {
                if (_entities.ContainsKey(entityId))
                {
                    Plugin.Log.LogWarning($"[EntitySync] Entity {entityId} already exists, updating.");
                    _entities[entityId] = new EntityRecord
                    {
                        EntityId = entityId,
                        EntityType = entityType,
                        Position = position,
                        Rotation = rotation,
                        Scale = scale,
                        OwnerPlayerId = playerId,
                        SpawnTick = MultiplayerSession.Tick
                    };
                }
                else
                {
                    _entities[entityId] = new EntityRecord
                    {
                        EntityId = entityId,
                        EntityType = entityType,
                        Position = position,
                        Rotation = rotation,
                        Scale = scale,
                        OwnerPlayerId = playerId,
                        SpawnTick = MultiplayerSession.Tick
                    };
                }
            }

            var spawnMsg = new MsgEntitySpawn
            {
                PlayerId = playerId,
                Tick = MultiplayerSession.Tick,
                EntityId = entityId,
                EntityType = entityType,
                Position = position.FromUnity(),
                Rotation = rotation.FromUnity(),
                Scale = scale.FromUnity()
            };

            Plugin.Log.LogInfo($"[EntitySync] Spawn: id={entityId}, type={entityType}, pos={position}");
            OnEntitySpawned?.Invoke(spawnMsg);
        }

        public static void RegisterDespawn(uint entityId, int playerId)
        {
            EntityRecord record;
            lock (_lock)
            {
                if (!_entities.TryGetValue(entityId, out record))
                {
                    Plugin.Log.LogWarning($"[EntitySync] Despawn unknown entity {entityId}");
                    return;
                }
                _entities.Remove(entityId);
            }

            var despawnMsg = new MsgEntityDespawn
            {
                PlayerId = playerId,
                Tick = MultiplayerSession.Tick,
                EntityId = entityId
            };

            Plugin.Log.LogInfo($"[EntitySync] Despawn: id={entityId}");
            OnEntityDespawned?.Invoke(despawnMsg);
        }

        public static void ApplyRemoteSpawn(MsgEntitySpawn msg)
        {
            lock (_lock)
            {
                if (_entities.ContainsKey(msg.EntityId))
                {
                    Plugin.Log.LogWarning($"[EntitySync] Remote spawn duplicate id={msg.EntityId}, overwriting.");
                }
                _entities[msg.EntityId] = new EntityRecord
                {
                    EntityId = msg.EntityId,
                    EntityType = msg.EntityType,
                    Position = msg.Position.ToUnity(),
                    Rotation = msg.Rotation.ToUnity(),
                    Scale = msg.Scale.ToUnity(),
                    OwnerPlayerId = msg.PlayerId,
                    SpawnTick = msg.Tick
                };
            }
            Plugin.Log.LogInfo($"[EntitySync] Applied remote spawn: id={msg.EntityId}, type={msg.EntityType}");
        }

        public static void ApplyRemoteDespawn(MsgEntityDespawn msg)
        {
            lock (_lock)
            {
                if (!_entities.Remove(msg.EntityId))
                    Plugin.Log.LogWarning($"[EntitySync] Remote despawn unknown id={msg.EntityId}");
            }
            Plugin.Log.LogInfo($"[EntitySync] Applied remote despawn: id={msg.EntityId}");
        }

        public static void ApplyFullStateSnapshot(MsgFullStateSnapshot snapshot)
        {
            Plugin.Log.LogInfo($"[EntitySync] Applying full state snapshot: tick={snapshot.Tick}, entities={snapshot.Entities.Count}");
            lock (_lock)
            {
                _entities.Clear();
                foreach (var entry in snapshot.Entities)
                {
                    _entities[entry.EntityId] = new EntityRecord
                    {
                        EntityId = entry.EntityId,
                        EntityType = entry.EntityType,
                        Position = entry.Position.ToUnity(),
                        Rotation = entry.Rotation.ToUnity(),
                        Scale = entry.Scale.ToUnity(),
                        OwnerPlayerId = entry.OwnerPlayerId,
                        SpawnTick = snapshot.Tick
                    };
                }
            }
        }

        public static MsgFullStateSnapshot BuildSnapshot()
        {
            var snapshot = new MsgFullStateSnapshot
            {
                Tick = MultiplayerSession.Tick,
                PlayerCount = MultiplayerSession.PlayerCount
            };

            lock (_lock)
            {
                foreach (var kv in _entities)
                {
                    snapshot.Entities.Add(new EntitySnapshotEntry
                    {
                        EntityId = kv.Value.EntityId,
                        EntityType = kv.Value.EntityType,
                        Position = kv.Value.Position.FromUnity(),
                        Rotation = kv.Value.Rotation.FromUnity(),
                        Scale = kv.Value.Scale.FromUnity(),
                        OwnerPlayerId = kv.Value.OwnerPlayerId
                    });
                }
            }

            Plugin.Log.LogInfo($"[EntitySync] Built snapshot: {snapshot.Entities.Count} entities");
            return snapshot;
        }

        public static uint AllocateEntityId()
        {
            lock (_lock) return _nextEntityId++;
        }

        public static bool TryGetEntity(uint entityId, out EntityRecord record)
        {
            lock (_lock) return _entities.TryGetValue(entityId, out record);
        }

        public static void ClearAll()
        {
            lock (_lock) _entities.Clear();
            Plugin.Log.LogInfo("[EntitySync] Cleared all entities");
        }
    }

    public class EntityRecord
    {
        public uint EntityId;
        public string EntityType;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
        public int OwnerPlayerId;
        public uint SpawnTick;
    }
}
