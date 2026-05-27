using System;
using System.Collections.Generic;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Performance
{
    public static class DeltaCompressor
    {
        static readonly Dictionary<uint, EntityTransformState> _lastStates = new Dictionary<uint, EntityTransformState>();
        static readonly object _lock = new object();

        const float PositionThreshold = 0.01f;
        const float RotationAngleThreshold = 0.5f;
        const float ScaleThreshold = 0.01f;

        public static bool IsSignificantDelta(uint entityId, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            lock (_lock)
            {
                if (!_lastStates.TryGetValue(entityId, out var last))
                {
                    _lastStates[entityId] = new EntityTransformState
                    {
                        Position = position,
                        Rotation = rotation,
                        Scale = scale
                    };
                    return true;
                }

                bool posDelta = Vector3.Distance(last.Position, position) > PositionThreshold;
                float rotAngle = Quaternion.Angle(last.Rotation, rotation);
                bool rotDelta = rotAngle > RotationAngleThreshold;
                bool scaleDelta = Vector3.Distance(last.Scale, scale) > ScaleThreshold;

                if (posDelta || rotDelta || scaleDelta)
                {
                    _lastStates[entityId] = new EntityTransformState
                    {
                        Position = position,
                        Rotation = rotation,
                        Scale = scale
                    };
                }

                return posDelta || rotDelta || scaleDelta;
            }
        }

        public static void UpdateState(uint entityId, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            lock (_lock)
            {
                _lastStates[entityId] = new EntityTransformState
                {
                    Position = position,
                    Rotation = rotation,
                    Scale = scale
                };
            }
        }

        public static void RemoveEntity(uint entityId)
        {
            lock (_lock)
            {
                _lastStates.Remove(entityId);
            }
        }

        public static void ClearAll()
        {
            lock (_lock) _lastStates.Clear();
        }
    }

    class EntityTransformState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }
}
