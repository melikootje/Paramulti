using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;
using UnityEngine;

namespace ParalivesMultiplayer.Patches
{
    static class PlayerStatePatches
    {
        static bool _enabled = true;
        static float _lastSyncTime;
        const float SyncInterval = 0.05f;

        static Transform _cachedLocalTransform;
        static bool _transformCacheValid;

        public static void Apply(Harmony harmony)
        {
            // State capture is handled via Plugin.Update() calling OnGameUpdate()
            // SystemManager.LateUpdate patches were removed because Harmony detours
            // on core game methods can cause Unity crashes at JIT time.
        }

        static void PatchSystemManagerLateUpdate(Harmony harmony)
        {
            try
            {
                var systemManagerType = ParalivesGameApiResolver.SystemManagerType;
                if (systemManagerType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var name = asm.GetName().Name;
                        if (name != null && name.Contains("Paralives"))
                        {
                            systemManagerType = asm.GetType("SystemManager");
                            if (systemManagerType != null) break;
                        }
                    }
                }

                if (systemManagerType == null)
                {
                    PatchLogger.LogWarning("SystemManager type not found, skipping player state patches.");
                    return;
                }

                var lateUpdate = AccessTools.Method(systemManagerType, "LateUpdate");
                if (lateUpdate == null)
                {
                    PatchLogger.LogWarning("SystemManager.LateUpdate method not found.");
                    return;
                }

                PatchLogger.SafePatch(harmony, lateUpdate,
                    new HarmonyMethod(typeof(PlayerStatePatches), nameof(OnSystemManagerLateUpdatePostfix)),
                    "SystemManager.LateUpdate");

                PatchLogger.Log($"[PlayerState] Patches applied to {systemManagerType.FullName}.LateUpdate");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply player state patches: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void OnSystemManagerLateUpdatePostfix()
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            var now = Time.time;
            if (now - _lastSyncTime < SyncInterval) return;
            _lastSyncTime = now;

            try
            {
                var transform = RemoteCharacterManager.LocalCharacterTransform;
                if (transform == null)
                {
                    RemoteCharacterManager.FindLocalCharacter();
                    transform = RemoteCharacterManager.LocalCharacterTransform;
                    if (transform == null) return;
                }

                _cachedLocalTransform = transform;
                _transformCacheValid = true;

                var pos = transform.position;
                var rot = transform.rotation;
                var vel = Vector3.zero;

                var msg = new Networking.Messages.MsgUpdateState
                {
                    Tick = MultiplayerSession.Tick,
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    Position = pos.FromUnity(),
                    Velocity = vel.FromUnity(),
                    Rotation = rot.FromUnity()
                };

                if (MultiplayerSession.IsHost)
                {
                    UdpNetworkManager.Instance?.SendToAllClients(msg);
                    PatchLogger.LogDebug($"[Paramulti][Local] Host captured local avatar transform. pos={pos}, rot={rot}");
                }
                else if (UdpNetworkManager.Instance != null)
                {
                    UdpNetworkManager.Instance.SendToHost(msg);
                    PatchLogger.LogDebug($"[Paramulti][Local] Client captured local avatar transform. pos={pos}");
                }
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Player state capture error: {ex.Message}");
            }
        }

        static Transform ExtractTransform(object obj)
        {
            if (obj == null) return null;

            var transform = obj as Transform;
            if (transform != null) return transform;

            var go = obj as GameObject;
            if (go != null) return go.transform;

            var component = obj as Component;
            if (component != null) return component.transform;

            var tType = obj.GetType();
            var transformProp = tType.GetProperty("transform", BindingFlags.Public | BindingFlags.Instance);
            if (transformProp != null)
            {
                try
                {
                    var val = transformProp.GetValue(obj);
                    if (val is Transform t) return t;
                }
                catch { }
            }

            var transformField = tType.GetField("transform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (transformField != null)
            {
                try
                {
                    var val = transformField.GetValue(obj);
                    if (val is Transform t2) return t2;
                }
                catch { }
            }

            var goProp = tType.GetProperty("gameObject", BindingFlags.Public | BindingFlags.Instance);
            if (goProp != null)
            {
                try
                {
                    var val = goProp.GetValue(obj);
                    if (val is GameObject g) return g.transform;
                }
                catch { }
            }

            var goField = tType.GetField("gameObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (goField != null)
            {
                try
                {
                    var val = goField.GetValue(obj);
                    if (val is GameObject g2) return g2.transform;
                }
                catch { }
            }

            return null;
        }

        public static void SetEnabled(bool value)
        {
            _enabled = value;
            PatchLogger.Log($"Player state sync {(value ? "enabled" : "disabled")}");
        }

        public static Transform GetCachedLocalTransform()
        {
            if (_transformCacheValid && _cachedLocalTransform != null)
                return _cachedLocalTransform;
            return null;
        }
    }
}
