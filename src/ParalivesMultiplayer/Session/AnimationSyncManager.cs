using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class AnimationSyncManager
    {
        static readonly Dictionary<int, object> _remoteAnimators = new Dictionary<int, object>();
        static readonly object _lock = new object();
        static float _lastSendTime;
        static bool _animatorLogged;
        const float SendInterval = 0.1f; // 10Hz animation updates

        public static bool Enabled { get; set; } = true;

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[AnimationSync] Initialized");
        }

        public static void Update()
        {
            if (!Enabled) return;
            if (!MultiplayerSession.IsActive) return;

            var now = Time.time;
            if (now - _lastSendTime < SendInterval) return;
            _lastSendTime = now;

            CaptureAndSendLocalAnimation();
        }

        // Find the CharacterAnimator custom component on a transform.
        static object FindCharacterAnimator(Transform root)
        {
            if (root == null) return null;
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                if (comp.GetType().Name == "CharacterAnimator") return comp;
            }
            return null;
        }

        // Read the first currently playing animation from a CharacterAnimator.
        static ulong GetCurrentAnimationGuid(object characterAnimator, out float normalizedTime, out float speed, out float weight)
        {
            normalizedTime = 0f;
            speed = 1f;
            weight = 1f;
            if (characterAnimator == null) return 0UL;
            try
            {
                var t = characterAnimator.GetType();
                var guidProp = t.GetProperty("CurrentlyPlayingAnimationGUID", BindingFlags.Public | BindingFlags.Instance);
                if (guidProp == null) return 0UL;
                ulong guid = (ulong)guidProp.GetValue(characterAnimator);
                if (guid == 0UL) return 0UL;

                var containersField = t.GetField("CurrentlyPlayingAnimationContainers",
                    BindingFlags.Public | BindingFlags.Instance);
                var containers = containersField?.GetValue(characterAnimator) as IList;
                if (containers != null && containers.Count > 0)
                {
                    var firstContainer = containers[0];
                    if (firstContainer != null)
                    {
                        var stateProp = firstContainer.GetType().GetProperty("CurrentAnimancerState",
                            BindingFlags.Public | BindingFlags.Instance);
                        var state = stateProp?.GetValue(firstContainer);
                        if (state != null)
                        {
                            var st = state.GetType();
                            var ntProp = st.GetProperty("NormalizedTime", BindingFlags.Public | BindingFlags.Instance);
                            if (ntProp != null) normalizedTime = (float)ntProp.GetValue(state);
                            var speedProp = st.GetProperty("Speed", BindingFlags.Public | BindingFlags.Instance);
                            if (speedProp != null) speed = (float)speedProp.GetValue(state);
                            var wProp = st.GetProperty("EffectiveWeight", BindingFlags.Public | BindingFlags.Instance);
                            if (wProp != null) weight = (float)wProp.GetValue(state);
                        }
                    }
                }
                return guid;
            }
            catch
            {
                return 0UL;
            }
        }

        static void CaptureAndSendLocalAnimation()
        {
            var localTransform = RemoteCharacterManager.LocalCharacterTransform;
            if (localTransform == null)
            {
                if (!_animatorLogged) Plugin.Log.LogInfo("[AnimationSync] LocalCharacterTransform is null, skipping");
                return;
            }

            var characterAnimator = FindCharacterAnimator(localTransform);
            if (characterAnimator == null)
            {
                if (!_animatorLogged)
                {
                    _animatorLogged = true;
                    Plugin.Log.LogInfo($"[AnimationSync] No CharacterAnimator found on {localTransform.gameObject.name}, childCount={localTransform.childCount}");
                    var components = localTransform.GetComponentsInChildren<Component>(true);
                    var compNames = new System.Collections.Generic.HashSet<string>();
                    foreach (var c in components) { if (c != null) compNames.Add(c.GetType().Name); }
                    Plugin.Log.LogInfo($"[AnimationSync] Component types found: {string.Join(",", compNames)}");
                }
                return;
            }

            ulong guid = GetCurrentAnimationGuid(characterAnimator, out float nt, out float speed, out float weight);
            if (guid == 0UL) return;

            var msg = new MsgAnimationState
            {
                Tick = MultiplayerSession.Tick,
                PlayerId = MultiplayerSession.LocalPlayerId,
                AnimatorStateHash = (int)guid,
                TransitionDestinationStateHash = (int)(guid >> 32),
                NormalizedTime = nt,
                Speed = speed,
                IsInTransition = false
            };

            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            if (MultiplayerSession.IsHost)
                net.SendToAllClients(msg);
            else
                net.SendToHost(msg);
        }

        public static void ReceiveAnimationState(MsgAnimationState msg)
        {
            if (msg.PlayerId == MultiplayerSession.LocalPlayerId) return;

            lock (_lock)
            {
                if (!_remoteAnimators.TryGetValue(msg.PlayerId, out var characterAnimator))
                {
                    var entry = RemoteCharacterManager.GetRemoteCharacterEntry(msg.PlayerId);
                    if (entry == null || entry.ControlledTransform == null) return;
                    characterAnimator = FindCharacterAnimator(entry.ControlledTransform);
                    if (characterAnimator != null)
                        _remoteAnimators[msg.PlayerId] = characterAnimator;
                }

                if (characterAnimator != null)
                    ApplyAnimationState(characterAnimator, msg);
            }
        }

        static void ApplyAnimationState(object characterAnimator, MsgAnimationState msg)
        {
            try
            {
                ulong guid = ((ulong)(uint)msg.AnimatorStateHash) |
                             (((ulong)(uint)msg.TransitionDestinationStateHash) << 32);
                if (guid == 0UL) return;

                var t = characterAnimator.GetType();
                var playMethod = t.GetMethod("PlayAnimation",
                    BindingFlags.Public | BindingFlags.Instance);
                if (playMethod == null) return;

                // PlayAnimation(animationAssetGUID, containerData=null, fadeDuration=0.1f, playSpeed=msg.Speed)
                playMethod.Invoke(characterAnimator, new object[] { guid, null, 0.1f, msg.Speed });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AnimationSync] Failed to apply animation to player {msg.PlayerId}: {ex.Message}");
            }
        }

        public static void RegisterRemoteAnimator(int playerId, object characterAnimator)
        {
            lock (_lock)
            {
                _remoteAnimators[playerId] = characterAnimator;
            }
        }

        public static void UnregisterRemoteAnimator(int playerId)
        {
            lock (_lock)
            {
                _remoteAnimators.Remove(playerId);
            }
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _remoteAnimators.Clear();
            }
        }
    }
}
