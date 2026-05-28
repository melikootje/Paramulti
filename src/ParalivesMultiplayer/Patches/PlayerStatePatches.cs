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
            PatchPlayerController(harmony);
            PatchUnityInput(harmony);
        }

        static void PatchPlayerController(Harmony harmony)
        {
            try
            {
                var playerType = null as Type;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (name == null) continue;

                    if (name.Contains("Paralives") || name.Contains("Assembly-CSharp"))
                    {
                        playerType = asm.GetType("PlayerController");
                        if (playerType != null) break;

                        playerType = asm.GetType("Player");
                        if (playerType != null) break;
                    }
                }

                if (playerType == null)
                {
                    PatchLogger.LogWarning("PlayerController/Player type not found, skipping player state patches.");
                    return;
                }

                var fixedUpdate = AccessTools.Method(playerType, "FixedUpdate");
                if (fixedUpdate != null)
                {
                    PatchLogger.SafePatch(harmony, fixedUpdate,
                        new HarmonyMethod(typeof(PlayerStatePatches), nameof(OnPlayerFixedUpdatePostfix)),
                        $"Player.{playerType.Name}.FixedUpdate");
                }

                var update = AccessTools.Method(playerType, "Update");
                if (update != null)
                {
                    PatchLogger.SafePatch(harmony, update,
                        new HarmonyMethod(typeof(PlayerStatePatches), nameof(OnPlayerUpdatePostfix)),
                        $"Player.{playerType.Name}.Update");
                }

                PatchLogger.Log($"[PlayerState] Patches applied to {playerType.FullName}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply player state patches: {ex.Message}");
            }
        }

        static void PatchUnityInput(Harmony harmony)
        {
            try
            {
                var inputType = AccessTools.TypeByName("UnityEngine.Input");
                if (inputType == null) return;

                PatchLogger.LogDebug("UnityEngine.Input type available for observation.");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to probe Unity Input: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void OnPlayerFixedUpdatePostfix(object __instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            var now = Time.time;
            if (now - _lastSyncTime < SyncInterval) return;
            _lastSyncTime = now;

            try
            {
                var transform = ExtractTransform(__instance);
                if (transform == null) return;

                _cachedLocalTransform = transform;
                _transformCacheValid = true;

                var pos = transform.position;
                var rot = transform.rotation;
                var vel = Vector3.zero;

                if (MultiplayerSession.IsHost)
                {
                    PatchLogger.LogDebug($"Host player state: pos={pos}, rot={rot}");
                }
                else if (TcpNetworkManager.Instance != null)
                {
                    var msg = new Networking.Messages.MsgUpdateState
                    {
                        Tick = MultiplayerSession.Tick,
                        PlayerId = MultiplayerSession.LocalPlayerId,
                        Position = pos.FromUnity(),
                        Velocity = vel.FromUnity(),
                        Rotation = rot.FromUnity()
                    };
                    TcpNetworkManager.Instance.SendToHost(msg);
                    PatchLogger.LogDebug($"Client sent state update: pos={pos}");
                }
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"PlayerFixedUpdatePostfix error: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void OnPlayerUpdatePostfix(object __instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                var transform = ExtractTransform(__instance);
                if (transform != null)
                {
                    _cachedLocalTransform = transform;
                    _transformCacheValid = true;
                    PatchLogger.LogDebug($"Player update observed: {transform.gameObject.name}");
                }
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"PlayerUpdatePostfix error: {ex.Message}");
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

            var monoBehaviour = obj as MonoBehaviour;
            if (monoBehaviour != null) return monoBehaviour.transform;

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
