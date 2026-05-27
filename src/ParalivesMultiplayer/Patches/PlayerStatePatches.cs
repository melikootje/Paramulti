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
                var go = __instance as GameObject;
                if (go == null) return;

                var pos = go.transform.position;
                var rot = go.transform.rotation;

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
                        Position = pos,
                        Rotation = rot
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
                var go = __instance as GameObject;
                if (go == null) return;

                PatchLogger.LogDebug($"Player update observed: {go.name}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"PlayerUpdatePostfix error: {ex.Message}");
            }
        }

        public static void SetEnabled(bool value)
        {
            _enabled = value;
            PatchLogger.Log($"Player state sync {(value ? "enabled" : "disabled")}");
        }
    }
}
