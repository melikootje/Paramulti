using System;
using System.Collections.Generic;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class PlayerSyncManager
    {
        static readonly Dictionary<int, PlayerStateBuffer> _buffers = new Dictionary<int, PlayerStateBuffer>();
        static readonly object _lock = new object();
        static int _bufferSize = 30;

        const float InterpDelaySec = 0.1f;
        public const float ExtrapolationLimitSec = 0.5f;

        public static bool Enabled { get; set; }

        public static event Action<int, Vector3, Quaternion> OnRemotePlayerRender;

        public static int RemotePlayerCount
        {
            get
            {
                lock (_lock) return _buffers.Count;
            }
        }

        public static void Initialize(int bufferSize = 30)
        {
            _bufferSize = bufferSize;
            ClearAll();
            Plugin.Log.LogInfo($"[PlayerSync] Initialized with buffer size {_bufferSize}");
        }

        public static void RegisterPlayer(int playerId)
        {
            lock (_lock)
            {
                _buffers[playerId] = new PlayerStateBuffer(_bufferSize);
            }
            Plugin.Log.LogInfo($"[PlayerSync] Registered player {playerId}");
        }

        public static void UnregisterPlayer(int playerId)
        {
            lock (_lock)
            {
                _buffers.Remove(playerId);
            }
            Plugin.Log.LogInfo($"[PlayerSync] Unregistered player {playerId}");
        }

        public static void EnqueueState(MsgUpdateState state)
        {
            if (!Enabled) return;

            lock (_lock)
            {
                if (_buffers.TryGetValue(state.PlayerId, out var buf))
                {
                    buf.AddState(state);
                }
                else
                {
                    var newBuf = new PlayerStateBuffer(_bufferSize);
                    newBuf.AddState(state);
                    _buffers[state.PlayerId] = newBuf;
                }
            }
        }

        public static void Update()
        {
            if (!Enabled) return;

            float now = Time.time;
            float targetTime = now - InterpDelaySec;

            lock (_lock)
            {
                foreach (var kv in _buffers)
                {
                    var buf = kv.Value;
                    if (buf.TryGetInterpolatedState(targetTime, out var pos, out var rot))
                    {
                        OnRemotePlayerRender?.Invoke(kv.Key, pos, rot);
                    }
                }
            }
        }

        public static void ClearAll()
        {
            lock (_lock) _buffers.Clear();
        }
    }

    class PlayerStateBuffer
    {
        readonly StateSnapshot[] _states;
        int _head;
        int _count;

        public PlayerStateBuffer(int capacity)
        {
            _states = new StateSnapshot[capacity];
            _head = 0;
            _count = 0;
        }

        public void AddState(MsgUpdateState msg)
        {
            var snapshot = new StateSnapshot
            {
                Tick = msg.Tick,
                Timestamp = Time.time,
                Position = msg.Position.ToUnity(),
                Rotation = msg.Rotation.ToUnity(),
                Velocity = msg.Velocity.ToUnity()
            };

            var writePos = (_head + _count) % _states.Length;
            _states[writePos] = snapshot;

            if (_count >= _states.Length)
            {
                _head = (_head + 1) % _states.Length;
            }
            else
            {
                _count++;
            }
        }

        public bool TryGetInterpolatedState(float targetTime, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (_count < 2) return false;

            StateSnapshot prev = default;
            StateSnapshot next = default;
            bool hasPrev = false;
            bool hasNext = false;

            for (int i = 0; i < _count; i++)
            {
                var idx = (_head + i) % _states.Length;
                var s = _states[idx];

                if (s.Timestamp <= targetTime)
                {
                    prev = s;
                    hasPrev = true;
                }
                else if (!hasNext && s.Timestamp > targetTime)
                {
                    next = s;
                    hasNext = true;
                    break;
                }
            }

            if (!hasPrev) return false;

            if (!hasNext)
            {
                if (Time.time - prev.Timestamp > 0.5f)
                {
                    Plugin.Log.LogWarning($"[PlayerSync] Extrapolation limit exceeded");
                    return false;
                }
                position = prev.Position + prev.Velocity * (targetTime - prev.Timestamp);
                rotation = prev.Rotation;
                return true;
            }

            float t = (targetTime - prev.Timestamp) / (next.Timestamp - prev.Timestamp);
            t = Mathf.Clamp01(t);

            position = Vector3.Lerp(prev.Position, next.Position, t);
            rotation = Quaternion.Slerp(prev.Rotation, next.Rotation, t);
            return true;
        }
    }

    struct StateSnapshot
    {
        public uint Tick;
        public float Timestamp;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
    }
}