using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Performance
{
    public static class ReconnectionManager
    {
        static readonly Dictionary<int, ClientSessionState> _clientStates = new Dictionary<int, ClientSessionState>();
        static readonly object _lock = new object();
        static Stopwatch _sw = new Stopwatch();

        static int _maxReconnectAttempts;
        static int _reconnectDelayMs;
        static int _sessionTtlSeconds;
        static bool _enabled;

        static int _currentReconnectAttempt;
        static int _disconnectedPlayerId;
        static string _disconnectedClientName;
        static uint _lastKnownTick;
        static uint _lastSequenceNumber;
        static bool _isReconnecting;
        static CancellationTokenSource _reconnectCts;

        public static event Action<int, string> OnReconnectSucceeded;
        public static event Action<int, string> OnReconnectFailed;

        static ReconnectionManager()
        {
            _sw.Start();
        }

        public static void Initialize(bool enabled, int maxAttempts, int delayMs, int ttlSeconds)
        {
            _enabled = enabled;
            _maxReconnectAttempts = maxAttempts;
            _reconnectDelayMs = delayMs;
            _sessionTtlSeconds = ttlSeconds;
            Plugin.Log.LogInfo($"[Reconnect] Initialized: enabled={enabled}, maxAttempts={maxAttempts}, delay={delayMs}ms, TTL={ttlSeconds}s");
        }

        public static bool IsEnabled => _enabled;
        public static bool IsReconnecting => _isReconnecting;

        public static void SaveClientState(int clientId, string clientName, uint lastTick, uint lastSequence)
        {
            if (!_enabled) return;
            lock (_lock)
            {
                _clientStates[clientId] = new ClientSessionState
                {
                    PlayerId = clientId,
                    ClientName = clientName,
                    LastKnownTick = lastTick,
                    LastSequenceNumber = lastSequence,
                    DisconnectTimeMs = _sw.ElapsedMilliseconds
                };
                Plugin.Log.LogInfo($"[Reconnect] Saved state for client {clientId} (tick={lastTick}, seq={lastSequence})");
            }
        }

        public static bool ValidateReconnectRequest(MsgReconnectRequest req, out MsgReconnectAck ack)
        {
            lock (_lock)
            {
                if (!_clientStates.TryGetValue(req.PlayerId, out var saved))
                {
                    ack = new MsgReconnectAck
                    {
                        PlayerId = req.PlayerId,
                        Allowed = false,
                        SessionTick = MultiplayerSession.Tick,
                        ErrorMessage = "No saved session state found"
                    };
                    return false;
                }

                long elapsedMs = _sw.ElapsedMilliseconds - saved.DisconnectTimeMs;
                if (elapsedMs > _sessionTtlSeconds * 1000L)
                {
                    _clientStates.Remove(req.PlayerId);
                    ack = new MsgReconnectAck
                    {
                        PlayerId = req.PlayerId,
                        Allowed = false,
                        SessionTick = MultiplayerSession.Tick,
                        ErrorMessage = $"Session expired ({elapsedMs}ms > {_sessionTtlSeconds * 1000}ms TTL)"
                    };
                    return false;
                }

                saved.LastKnownTick = req.LastKnownTick;
                saved.LastSequenceNumber = req.LastSequenceNumber;
                saved.ReconnectTimeMs = _sw.ElapsedMilliseconds;

                ack = new MsgReconnectAck
                {
                    PlayerId = req.PlayerId,
                    Allowed = true,
                    SessionTick = MultiplayerSession.Tick,
                    ErrorMessage = ""
                };
                return true;
            }
        }

        public static void StartClientReconnect(int playerId, string clientName, uint lastTick, uint lastSequence)
        {
            if (!_enabled) return;
            _isReconnecting = true;
            _currentReconnectAttempt = 0;
            _disconnectedPlayerId = playerId;
            _disconnectedClientName = clientName;
            _lastKnownTick = lastTick;
            _lastSequenceNumber = lastSequence;

            Plugin.Log.LogInfo($"[Reconnect] Starting reconnect for player {playerId}");
            ScheduleReconnectAttempt();
        }

        static void ScheduleReconnectAttempt()
        {
            if (_currentReconnectAttempt >= _maxReconnectAttempts)
            {
                _isReconnecting = false;
                OnReconnectFailed?.Invoke(_disconnectedPlayerId, $"Max attempts ({_maxReconnectAttempts}) reached");
                Plugin.Log.LogWarning($"[Reconnect] Failed after {_maxReconnectAttempts} attempts for player {_disconnectedPlayerId}");
                return;
            }

            int delay = _reconnectDelayMs * (1 << _currentReconnectAttempt);
            if (delay > 30000) delay = 30000;

            Plugin.Log.LogInfo($"[Reconnect] Attempt {_currentReconnectAttempt + 1}/{_maxReconnectAttempts} in {delay}ms");

            System.Threading.Tasks.Task.Run(() =>
            {
                Thread.Sleep(delay);
                _currentReconnectAttempt++;
                TryReconnect();
            });
        }

        static void TryReconnect()
        {
            var net = UdpNetworkManager.Instance;
            if (net == null)
            {
                _isReconnecting = false;
                OnReconnectFailed?.Invoke(_disconnectedPlayerId, "Network manager unavailable");
                return;
            }

            try
            {
                var req = new MsgReconnectRequest
                {
                    PlayerId = _disconnectedPlayerId,
                    ClientName = _disconnectedClientName,
                    LastKnownTick = _lastKnownTick,
                    LastSequenceNumber = _lastSequenceNumber
                };

                net.SendToHost(req);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Reconnect] Attempt failed: {ex.Message}");
                ScheduleReconnectAttempt();
            }
        }

        public static void OnReconnectAckReceived(MsgReconnectAck ack)
        {
            if (ack.Allowed)
            {
                _isReconnecting = false;
                OnReconnectSucceeded?.Invoke(ack.PlayerId, $"Reconnected at tick {ack.SessionTick}");
                Plugin.Log.LogInfo($"[Reconnect] Player {ack.PlayerId} reconnected successfully");
            }
            else
            {
                _isReconnecting = false;
                OnReconnectFailed?.Invoke(ack.PlayerId, ack.ErrorMessage);
                Plugin.Log.LogWarning($"[Reconnect] Rejected for player {ack.PlayerId}: {ack.ErrorMessage}");
            }
        }

        public static void CancelReconnect()
        {
            _isReconnecting = false;
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
        }

        public static void CleanExpiredStates()
        {
            long now = _sw.ElapsedMilliseconds;
            lock (_lock)
            {
                var expired = new List<int>();
                foreach (var kv in _clientStates)
                {
                    if (now - kv.Value.DisconnectTimeMs > _sessionTtlSeconds * 1000L)
                        expired.Add(kv.Key);
                }
                foreach (var id in expired)
                {
                    _clientStates.Remove(id);
                    Plugin.Log.LogInfo($"[Reconnect] Cleaned expired state for client {id}");
                }
            }
        }

        public static void ClearAll()
        {
            CancelReconnect();
            lock (_lock) _clientStates.Clear();
            Plugin.Log.LogInfo("[Reconnect] Cleared all reconnection state");
        }
    }

    class ClientSessionState
    {
        public int PlayerId;
        public string ClientName;
        public uint LastKnownTick;
        public uint LastSequenceNumber;
        public long DisconnectTimeMs;
        public long ReconnectTimeMs;
    }
}
