using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace ParalivesMultiplayer.Performance
{
    public static class SessionWatchdog
    {
        static readonly ConcurrentDictionary<string, ThreadState> _threads = new ConcurrentDictionary<string, ThreadState>();
        static readonly object _lock = new object();
        static Stopwatch _sw = new Stopwatch();
        static Timer _watchdogTimer;

        static int _checkIntervalMs;
        static int _stuckThresholdMs;
        static bool _enabled;

        public static event Action<string> OnThreadStuckDetected;

        static SessionWatchdog()
        {
            _sw.Start();
        }

        public static void Initialize(bool enabled, int checkIntervalMs, int stuckThresholdMs)
        {
            _enabled = enabled;
            _checkIntervalMs = checkIntervalMs;
            _stuckThresholdMs = stuckThresholdMs;

            if (_enabled)
            {
                _watchdogTimer = new Timer(WatchdogTick, null, checkIntervalMs, checkIntervalMs);
                Plugin.Log.LogInfo($"[Watchdog] Initialized: interval={checkIntervalMs}ms, threshold={stuckThresholdMs}ms");
            }
        }

        public static bool IsEnabled => _enabled;

        public static void RegisterThread(string name, Thread thread)
        {
            if (!_enabled) return;
            _threads[name] = new ThreadState
            {
                Name = name,
                Thread = thread,
                LastAliveTime = _sw.ElapsedMilliseconds,
                IsAlive = true
            };
        }

        public static void UnregisterThread(string name)
        {
            _threads.TryRemove(name, out _);
        }

        public static void Heartbeat(string name)
        {
            if (!_enabled) return;
            if (_threads.TryGetValue(name, out var state))
            {
                state.LastAliveTime = _sw.ElapsedMilliseconds;
                state.IsAlive = true;
            }
        }

        static void WatchdogTick(object state)
        {
            long now = _sw.ElapsedMilliseconds;

            foreach (var kv in _threads)
            {
                var threadState = kv.Value;
                long elapsed = now - threadState.LastAliveTime;

                if (elapsed > _stuckThresholdMs && threadState.IsAlive)
                {
                    threadState.IsAlive = false;
                    OnThreadStuckDetected?.Invoke(kv.Key);
                    Plugin.Log.LogError($"[Watchdog] Thread stuck: {kv.Key} (last alive {elapsed}ms ago)");

                    try
                    {
                        if (threadState.Thread != null && threadState.Thread.IsAlive)
                        {
                            Plugin.Log.LogWarning($"[Watchdog] Attempting to interrupt stuck thread: {kv.Key}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[Watchdog] Error during thread recovery: {ex.Message}");
                    }
                }
            }
        }

        public static void Dispose()
        {
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
        }

        public static void ClearAll()
        {
            Dispose();
            foreach (var key in _threads.Keys)
                _threads.TryRemove(key, out _);
        }
    }

    class ThreadState
    {
        public string Name;
        public Thread Thread;
        public long LastAliveTime;
        public bool IsAlive;
    }
}
