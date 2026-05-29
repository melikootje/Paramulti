using System;
using System.Collections.Generic;
using System.Diagnostics;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Performance
{
    public static class DesyncDetector
    {
        static readonly Dictionary<int, PlayerHeartbeatState> _players = new Dictionary<int, PlayerHeartbeatState>();
        static readonly object _lock = new object();

        const uint MaxTickDrift = 10;
        const uint MaxSequenceGap = 5;
        const int HeartbeatTimeoutMs = 3000;
        const int HeartbeatIntervalMs = 500;

        static Stopwatch _sw = new Stopwatch();

        public static event Action<int, string> OnDesyncDetected;

        static DesyncDetector()
        {
            _sw.Start();
        }

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[Desync] DesyncDetector initialized");
        }

        public static void BuildHeartbeat(out MsgHeartbeat msg)
        {
            msg = new MsgHeartbeat
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                Tick = MultiplayerSession.Tick,
                SequenceNumber = AllocateSequence(),
                TimestampMs = _sw.ElapsedMilliseconds
            };
        }

        public static void ProcessHeartbeat(MsgHeartbeat hb)
        {
            Plugin.Log.LogInfo($"[HeartbeatDebug] ProcessHeartbeat received: playerId={hb.PlayerId}, tick={hb.Tick}, seq={hb.SequenceNumber}, ts={hb.TimestampMs}, localTick={MultiplayerSession.Tick}");
            lock (_lock)
            {
                if (!_players.TryGetValue(hb.PlayerId, out var state))
                {
                    state = new PlayerHeartbeatState
                    {
                        PlayerId = hb.PlayerId,
                        LastTick = 0,
                        LastSequence = 0,
                        LastTimestampMs = _sw.ElapsedMilliseconds,
                        MissedHeartbeats = 0
                    };
                    _players[hb.PlayerId] = state;
                    Plugin.Log.LogInfo($"[HeartbeatDebug] ProcessHeartbeat: created new state for player {hb.PlayerId}");
                }

                state.LastTick = hb.Tick;
                state.LastSequence = hb.SequenceNumber;
                state.LastTimestampMs = hb.TimestampMs;
                state.MissedHeartbeats = 0;

                uint tickDrift = (uint)Math.Abs((long)hb.Tick - (long)MultiplayerSession.Tick);
                if (tickDrift > MaxTickDrift)
                {
                    var reason = $"Tick drift: local={MultiplayerSession.Tick}, remote={hb.Tick}, delta={tickDrift}";
                    Plugin.Log.LogWarning($"[Desync] {reason} for player {hb.PlayerId}");
                    OnDesyncDetected?.Invoke(hb.PlayerId, reason);
                }

                if (state.LastSequence > 0 && hb.SequenceNumber > state.LastSequence)
                {
                    uint gap = hb.SequenceNumber - state.LastSequence;
                    if (gap > MaxSequenceGap)
                    {
                        var reason = $"Sequence gap: {gap} (expected sequential, got {state.LastSequence} -> {hb.SequenceNumber})";
                        Plugin.Log.LogWarning($"[Desync] {reason} for player {hb.PlayerId}");
                        OnDesyncDetected?.Invoke(hb.PlayerId, reason);
                    }
                }

                state.LastSequence = hb.SequenceNumber;
            }
        }

        public static void TickCheck()
        {
            long now = _sw.ElapsedMilliseconds;
            lock (_lock)
            {
                foreach (var kv in _players)
                {
                    var state = kv.Value;
                    long timeSinceLastHb = now - state.LastTimestampMs;

                    if (timeSinceLastHb > HeartbeatTimeoutMs)
                    {
                        state.MissedHeartbeats++;
                        int missed = state.MissedHeartbeats;
                        Plugin.Log.LogWarning($"[Desync] Player {state.PlayerId} missed {missed} heartbeats ({timeSinceLastHb}ms since last)");

                        if (missed >= 6)
                        {
                            var reason = $"Heartbeat timeout after {missed} missed beats";
                            OnDesyncDetected?.Invoke(state.PlayerId, reason);
                        }
                    }
                    else if (timeSinceLastHb > HeartbeatIntervalMs * 1.5)
                    {
                        state.MissedHeartbeats++;
                    }
                }
            }
        }

        public static void RegisterPlayer(int playerId)
        {
            lock (_lock)
            {
                _players[playerId] = new PlayerHeartbeatState
                {
                    PlayerId = playerId,
                    LastTick = 0,
                    LastSequence = 0,
                    LastTimestampMs = _sw.ElapsedMilliseconds,
                    MissedHeartbeats = 0
                };
            }
        }

        public static void UnregisterPlayer(int playerId)
        {
            lock (_lock)
            {
                _players.Remove(playerId);
            }
        }

        static int _nextSeq;
        static uint AllocateSequence()
        {
            return (uint)System.Threading.Interlocked.Increment(ref _nextSeq);
        }

        public static void ClearAll()
        {
            lock (_lock) _players.Clear();
            Plugin.Log.LogInfo("[Desync] Cleared all player heartbeat state");
        }
    }

    class PlayerHeartbeatState
    {
        public int PlayerId;
        public uint LastTick;
        public uint LastSequence;
        public long LastTimestampMs;
        public int MissedHeartbeats;
    }
}
