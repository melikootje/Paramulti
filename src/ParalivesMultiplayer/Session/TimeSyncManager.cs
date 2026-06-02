using System;
using System.Reflection;
using UnityEngine;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Session
{
    // Host broadcasts ParaTime state; clients snap their local ParaTime to the host's value.
    // ParaTime is a static class in Paralives.dll with public static fields/properties:
    //   TotalMinutes, TimeSpeedIndex, IsPausedByPlayer, IsPausedByUI.
    // We use reflection so we don't need a compile-time reference to the type.
    public static class TimeSyncManager
    {
        const float BroadcastInterval = 1.0f; // seconds

        static Type _paraTimeType;
        static FieldInfo _totalMinutesField;
        static PropertyInfo _timeSpeedIndexProp;
        static PropertyInfo _isPausedByPlayerProp;
        static PropertyInfo _isPausedByUIProp;
        static bool _resolved;
        static bool _resolutionLogged;
        static float _lastBroadcast;
        static int _broadcastCount;
        static int _applyCount;

        static void Resolve()
        {
            if (_resolved) return;
            _paraTimeType = ParalivesGameApiResolver.ResolveType("ParaTime");
            if (_paraTimeType == null) return;

            _totalMinutesField = _paraTimeType.GetField("TotalMinutes",
                BindingFlags.Public | BindingFlags.Static);
            _timeSpeedIndexProp = _paraTimeType.GetProperty("TimeSpeedIndex",
                BindingFlags.Public | BindingFlags.Static);
            _isPausedByPlayerProp = _paraTimeType.GetProperty("IsPausedByPlayer",
                BindingFlags.Public | BindingFlags.Static);
            _isPausedByUIProp = _paraTimeType.GetProperty("IsPausedByUI",
                BindingFlags.Public | BindingFlags.Static);

            _resolved = _totalMinutesField != null;
            if (!_resolutionLogged)
            {
                _resolutionLogged = true;
                Plugin.Log.LogInfo($"[TimeSync] ParaTime resolution: type={_paraTimeType != null}, " +
                    $"TotalMinutes={_totalMinutesField != null}, TimeSpeedIndex={_timeSpeedIndexProp != null}, " +
                    $"IsPausedByPlayer={_isPausedByPlayerProp != null}, IsPausedByUI={_isPausedByUIProp != null}");
            }
        }

        // Called from Plugin.Update. Host-only.
        public static void TryBroadcastIfHost()
        {
            if (!MultiplayerSession.IsActive || !MultiplayerSession.IsHost) return;
            Resolve();
            if (!_resolved) return;

            float now = Time.time;
            if (now - _lastBroadcast < BroadcastInterval) return;
            _lastBroadcast = now;

            try
            {
                var msg = new MsgTimeSync
                {
                    TotalMinutes = (float)_totalMinutesField.GetValue(null),
                    TimeSpeedIndex = _timeSpeedIndexProp != null ? (int)_timeSpeedIndexProp.GetValue(null) : 0,
                    IsPausedByPlayer = _isPausedByPlayerProp != null && (bool)_isPausedByPlayerProp.GetValue(null),
                    IsPausedByUI = _isPausedByUIProp != null && (bool)_isPausedByUIProp.GetValue(null)
                };
                var net = Networking.TcpNetworkManager.Instance;
                net?.SendToAllClients(msg);
                _broadcastCount++;
                if (_broadcastCount <= 3 || _broadcastCount % 30 == 0)
                    Plugin.Log.LogInfo($"[TimeSync] broadcast #{_broadcastCount} min={msg.TotalMinutes:0.00}, speed={msg.TimeSpeedIndex}, pauseP={msg.IsPausedByPlayer}, pauseUI={msg.IsPausedByUI}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[TimeSync] broadcast failed: {ex.Message}");
            }
        }

        // Called from TcpNetworkManager on incoming MsgTimeSync. Client-side.
        public static void Apply(MsgTimeSync msg)
        {
            if (msg == null) return;
            if (MultiplayerSession.IsHost) return; // ignore on host
            Resolve();
            if (!_resolved) return;

            try
            {
                _totalMinutesField.SetValue(null, msg.TotalMinutes);
                _timeSpeedIndexProp?.SetValue(null, msg.TimeSpeedIndex);
                _isPausedByPlayerProp?.SetValue(null, msg.IsPausedByPlayer);
                _isPausedByUIProp?.SetValue(null, msg.IsPausedByUI);
                _applyCount++;
                if (_applyCount <= 3 || _applyCount % 30 == 0)
                    Plugin.Log.LogInfo($"[TimeSync] applied #{_applyCount} min={msg.TotalMinutes:0.00}, speed={msg.TimeSpeedIndex}, pauseP={msg.IsPausedByPlayer}, pauseUI={msg.IsPausedByUI}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[TimeSync] apply failed: {ex.Message}");
            }
        }
    }
}
