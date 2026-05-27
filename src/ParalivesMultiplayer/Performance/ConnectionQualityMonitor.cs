using System;
using System.Collections.Generic;
using System.Diagnostics;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Performance
{
    public static class ConnectionQualityMonitor
    {
        static readonly Dictionary<int, ClientQualityState> _clients = new Dictionary<int, ClientQualityState>();
        static readonly object _lock = new object();
        static Stopwatch _sw = new Stopwatch();

        static int _degradedThresholdMs;
        static int _criticalThresholdMs;
        static bool _enabled;

        public enum QualityLevel { Excellent, Good, Degraded, Critical, Offline }

        static ConnectionQualityMonitor()
        {
            _sw.Start();
        }

        public static void Initialize(bool enabled, int degradedMs, int criticalMs)
        {
            _enabled = enabled;
            _degradedThresholdMs = degradedMs;
            _criticalThresholdMs = criticalMs;
            Plugin.Log.LogInfo($"[ConnQuality] Initialized: enabled={enabled}, degraded={degradedMs}ms, critical={criticalMs}ms");
        }

        public static bool IsEnabled => _enabled;

        public static void RegisterClient(int clientId)
        {
            lock (_lock)
            {
                if (!_clients.ContainsKey(clientId))
                {
                    _clients[clientId] = new ClientQualityState
                    {
                        ClientId = clientId,
                        TotalPingsSent = 0,
                        TotalPongsReceived = 0,
                        LastPingTime = _sw.ElapsedMilliseconds
                    };
                }
            }
        }

        public static void UnregisterClient(int clientId)
        {
            lock (_lock) _clients.Remove(clientId);
        }

        public static void RecordPingSent(int clientId)
        {
            lock (_lock)
            {
                if (_clients.TryGetValue(clientId, out var state))
                {
                    state.TotalPingsSent++;
                    state.LastPingTime = _sw.ElapsedMilliseconds;
                    state.PendingPingTimestamp = _sw.ElapsedMilliseconds;
                }
            }
        }

        public static void RecordPongReceived(int clientId, long roundTripMs)
        {
            lock (_lock)
            {
                if (_clients.TryGetValue(clientId, out var state))
                {
                    state.TotalPongsReceived++;
                    state.LastRoundTripMs = roundTripMs;

                    if (state.PingHistory.Count >= 32)
                        state.PingHistory.Dequeue();
                    state.PingHistory.Enqueue(roundTripMs);

                    state.LastUpdateTime = _sw.ElapsedMilliseconds;
                }
            }
        }

        public static void RecordPacketLoss(int clientId)
        {
            lock (_lock)
            {
                if (_clients.TryGetValue(clientId, out var state))
                {
                    state.PacketLosses++;
                }
            }
        }

        public static QualityLevel GetQualityLevel(int clientId)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var state))
                    return QualityLevel.Offline;

                long now = _sw.ElapsedMilliseconds;
                long timeSinceUpdate = now - state.LastUpdateTime;

                if (timeSinceUpdate > 10000)
                    return QualityLevel.Offline;

                double avgPing = GetAveragePing(clientId);

                if (avgPing < _degradedThresholdMs * 0.5)
                    return QualityLevel.Excellent;
                if (avgPing < _degradedThresholdMs)
                    return QualityLevel.Good;
                if (avgPing < _criticalThresholdMs)
                    return QualityLevel.Degraded;
                return QualityLevel.Critical;
            }
        }

        public static double GetAveragePing(int clientId)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var state))
                    return -1;

                if (state.PingHistory.Count == 0)
                    return state.LastRoundTripMs >= 0 ? state.LastRoundTripMs : -1;

                double sum = 0;
                foreach (var p in state.PingHistory)
                    sum += p;
                return sum / state.PingHistory.Count;
            }
        }

        public static double GetPacketLossRate(int clientId)
        {
            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var state))
                    return -1;

                int total = state.TotalPingsSent;
                if (total == 0) return 0;

                int lost = total - state.TotalPongsReceived;
                if (lost < 0) lost = 0;
                return (double)lost / total;
            }
        }

        public static string GetQualityString(int clientId)
        {
            var level = GetQualityLevel(clientId);
            double avgPing = GetAveragePing(clientId);
            double lossRate = GetPacketLossRate(clientId);

            string levelStr = level switch
            {
                QualityLevel.Excellent => "Excellent",
                QualityLevel.Good => "Good",
                QualityLevel.Degraded => "Degraded",
                QualityLevel.Critical => "Critical",
                _ => "Offline"
            };

            return $"{levelStr} | Ping: {(avgPing >= 0 ? avgPing.ToString("F1") : "N/A")}ms | Loss: {(lossRate * 100):F1}%";
        }

        public static void ClearAll()
        {
            lock (_lock) _clients.Clear();
        }
    }

    class ClientQualityState
    {
        public int ClientId;
        public int TotalPingsSent;
        public int TotalPongsReceived;
        public int PacketLosses;
        public long LastPingTime;
        public long PendingPingTimestamp;
        public long LastUpdateTime;
        public double LastRoundTripMs = -1;
        public readonly Queue<double> PingHistory = new Queue<double>(32);
    }
}
