using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ParalivesMultiplayer.Performance
{
    public static class RateLimiter
    {
        static readonly Dictionary<int, ClientRateState> _clients = new Dictionary<int, ClientRateState>();
        static readonly object _lock = new object();
        static Stopwatch _sw = new Stopwatch();

        static int _maxMessagesPerSecond;
        static bool _enabled;

        static RateLimiter()
        {
            _sw.Start();
        }

        public static void Initialize(bool enabled, int maxMsgPerSec)
        {
            _enabled = enabled;
            _maxMessagesPerSecond = maxMsgPerSec;
            Plugin.Log.LogInfo($"[RateLimit] Initialized: enabled={enabled}, maxMsg/s={maxMsgPerSec}");
        }

        public static bool IsEnabled => _enabled;

        public static bool AllowMessage(int clientId)
        {
            if (!_enabled) return true;

            long now = _sw.ElapsedMilliseconds;

            lock (_lock)
            {
                if (!_clients.TryGetValue(clientId, out var state))
                {
                    state = new ClientRateState
                    {
                        ClientId = clientId,
                        WindowStart = now,
                        MessageCount = 0
                    };
                    _clients[clientId] = state;
                }

                if (now - state.WindowStart >= 1000)
                {
                    state.WindowStart = now;
                    state.MessageCount = 0;
                }

                state.MessageCount++;

                if (state.MessageCount > _maxMessagesPerSecond)
                {
                    state.RateLimited = true;
                    Plugin.Log.LogWarning($"[RateLimit] Client {clientId} exceeded rate limit ({state.MessageCount}/{_maxMessagesPerSecond}/s)");
                    return false;
                }

                state.RateLimited = false;
                return true;
            }
        }

        public static void RegisterClient(int clientId)
        {
            lock (_lock)
            {
                if (!_clients.ContainsKey(clientId))
                {
                    _clients[clientId] = new ClientRateState
                    {
                        ClientId = clientId,
                        WindowStart = _sw.ElapsedMilliseconds,
                        MessageCount = 0
                    };
                }
            }
        }

        public static void UnregisterClient(int clientId)
        {
            lock (_lock) _clients.Remove(clientId);
        }

        public static void ClearAll()
        {
            lock (_lock) _clients.Clear();
        }
    }

    class ClientRateState
    {
        public int ClientId;
        public long WindowStart;
        public int MessageCount;
        public bool RateLimited;
    }
}
