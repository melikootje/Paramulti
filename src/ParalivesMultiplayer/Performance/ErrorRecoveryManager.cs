using System;
using System.Collections.Generic;
using System.Diagnostics;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Performance
{
    public static class ErrorRecoveryManager
    {
        static readonly Dictionary<int, PlayerErrorState> _playerErrors = new Dictionary<int, PlayerErrorState>();
        static readonly object _lock = new object();
        static Stopwatch _sw = new Stopwatch();

        static int _circuitBreakerThreshold;
        static int _circuitBreakerResetMs;
        static bool _enabled;
        static bool _circuitOpen;
        static long _circuitOpenedAt;

        public enum CircuitState { Closed, HalfOpen, Open }
        public static CircuitState CurrentCircuitState => _circuitOpen ? CircuitState.Open : (_recentErrors > _circuitBreakerThreshold / 2 ? CircuitState.HalfOpen : CircuitState.Closed);

        static int _recentErrors;
        static long _errorWindowStart;

        public static event Action<bool> OnCircuitStateChanged;
        public static event Action<int, string> OnPlayerErrorThreshold;

        static ErrorRecoveryManager()
        {
            _sw.Start();
        }

        public static void Initialize(bool enabled, int threshold, int resetMs)
        {
            _enabled = enabled;
            _circuitBreakerThreshold = threshold;
            _circuitBreakerResetMs = resetMs;
            _circuitOpen = false;
            _recentErrors = 0;
            _errorWindowStart = _sw.ElapsedMilliseconds;
            Plugin.Log.LogInfo($"[ErrorRecovery] Initialized: enabled={enabled}, threshold={threshold}, reset={resetMs}ms");
        }

        public static bool IsEnabled => _enabled;
        public static bool IsCircuitOpen => _circuitOpen;

        public static bool ValidateMessage(MessageBase msg)
        {
            if (msg == null) return false;

            if (string.IsNullOrEmpty(msg.MessageCode))
            {
                Plugin.Log.LogWarning("[ErrorRecovery] Message with empty code rejected");
                return false;
            }

            if (msg.SenderClientId < -1)
            {
                Plugin.Log.LogWarning($"[ErrorRecovery] Invalid sender ID {msg.SenderClientId}");
                return false;
            }

            return true;
        }

        public static void RecordError(int playerId, string reason)
        {
            if (!_enabled) return;

            long now = _sw.ElapsedMilliseconds;
            lock (_lock)
            {
                _recentErrors++;
                if (now - _errorWindowStart > _circuitBreakerResetMs)
                {
                    _recentErrors = 1;
                    _errorWindowStart = now;
                }

                if (!_playerErrors.TryGetValue(playerId, out var state))
                {
                    state = new PlayerErrorState
                    {
                        PlayerId = playerId,
                        ErrorCount = 0,
                        LastErrorTime = now,
                        LastReason = ""
                    };
                    _playerErrors[playerId] = state;
                }

                state.ErrorCount++;
                state.LastErrorTime = now;
                state.LastReason = reason;

                if (state.ErrorCount >= 10)
                {
                    OnPlayerErrorThreshold?.Invoke(playerId, $"{reason} (count={state.ErrorCount})");
                    Plugin.Log.LogWarning($"[ErrorRecovery] Player {playerId} error threshold reached: {reason}");
                }

                if (_recentErrors >= _circuitBreakerThreshold && !_circuitOpen)
                {
                    OpenCircuit();
                }
            }

            PacketStats.RecordError();
        }

        static void OpenCircuit()
        {
            _circuitOpen = true;
            _circuitOpenedAt = _sw.ElapsedMilliseconds;
            OnCircuitStateChanged?.Invoke(true);
            Plugin.Log.LogError("[ErrorRecovery] Circuit breaker OPENED — sync paused");
        }

        public static void TryCloseCircuit()
        {
            if (!_circuitOpen) return;

            long elapsed = _sw.ElapsedMilliseconds - _circuitOpenedAt;
            if (elapsed < _circuitBreakerResetMs) return;

            lock (_lock)
            {
                if (_circuitOpen)
                {
                    _circuitOpen = false;
                    _recentErrors = 0;
                    _errorWindowStart = _sw.ElapsedMilliseconds;
                    OnCircuitStateChanged?.Invoke(false);
                    Plugin.Log.LogInfo("[ErrorRecovery] Circuit breaker CLOSED — sync resumed");
                }
            }
        }

        public static bool ShouldProcessMessage()
        {
            if (!_enabled) return true;
            TryCloseCircuit();
            return !_circuitOpen;
        }

        public static void RequestFullStateResync(int playerId)
        {
            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            var req = new MsgRequestFullState
            {
                PlayerId = playerId
            };

            if (ParalivesMultiplayer.Session.MultiplayerSession.IsHost)
            {
                net.SendToClient(playerId, ParalivesMultiplayer.Session.EntitySyncManager.BuildSnapshot());
            }
            else
            {
                net.SendToHost(req);
            }

            Plugin.Log.LogInfo($"[ErrorRecovery] Requested full state resync for player {playerId}");
        }

        public static void ClearPlayerErrors(int playerId)
        {
            lock (_lock) _playerErrors.Remove(playerId);
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _playerErrors.Clear();
                _recentErrors = 0;
                _circuitOpen = false;
            }
            Plugin.Log.LogInfo("[ErrorRecovery] Cleared all error state");
        }
    }

    class PlayerErrorState
    {
        public int PlayerId;
        public int ErrorCount;
        public long LastErrorTime;
        public string LastReason;
    }
}
