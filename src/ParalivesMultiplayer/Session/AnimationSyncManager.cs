using System.Collections.Generic;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class AnimationSyncManager
    {
        static readonly Dictionary<int, Animator> _remoteAnimators = new Dictionary<int, Animator>();
        static readonly object _lock = new object();
        static float _lastSendTime;
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

        static void CaptureAndSendLocalAnimation()
        {
            var localTransform = RemoteCharacterManager.LocalCharacterTransform;
            if (localTransform == null)
            {
                Plugin.Log.LogInfo("[AnimationSync] LocalCharacterTransform is null, skipping");
                return;
            }

            var animator = localTransform.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Plugin.Log.LogInfo($"[AnimationSync] No Animator found on {localTransform.gameObject.name} (children={localTransform.childCount})");
                return;
            }
            if (!animator.isActiveAndEnabled)
            {
                Plugin.Log.LogInfo($"[AnimationSync] Animator on {animator.gameObject.name} is not active/enabled");
                return;
            }

            var currentState = animator.GetCurrentAnimatorStateInfo(0);
            var transition = animator.IsInTransition(0);

            var msg = new MsgAnimationState
            {
                Tick = MultiplayerSession.Tick,
                PlayerId = MultiplayerSession.LocalPlayerId,
                AnimatorStateHash = currentState.shortNameHash,
                NormalizedTime = currentState.normalizedTime,
                Speed = currentState.speed,
                IsInTransition = transition,
                TransitionDestinationStateHash = transition ? animator.GetNextAnimatorStateInfo(0).shortNameHash : 0
            };

            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            if (MultiplayerSession.IsHost)
                net.SendToAllClients(msg);
            else
                net.SendToHost(msg);

            Plugin.Log.LogInfo($"[AnimationSync] Sent animation state: hash={msg.AnimatorStateHash}, time={msg.NormalizedTime:F2}, transition={msg.IsInTransition}");
        }

        public static void ReceiveAnimationState(MsgAnimationState msg)
        {
            if (msg.PlayerId == MultiplayerSession.LocalPlayerId) return;

            lock (_lock)
            {
                if (!_remoteAnimators.TryGetValue(msg.PlayerId, out var animator))
                {
                    animator = FindRemoteAnimator(msg.PlayerId);
                    if (animator != null)
                        _remoteAnimators[msg.PlayerId] = animator;
                }

                if (animator != null && animator.isActiveAndEnabled)
                {
                    ApplyAnimationState(animator, msg);
                }
            }
        }

        static Animator FindRemoteAnimator(int playerId)
        {
            var entry = RemoteCharacterManager.GetRemoteCharacterEntry(playerId);
            if (entry == null || entry.ControlledTransform == null) return null;

            return entry.ControlledTransform.GetComponentInChildren<Animator>(true);
        }

        static void ApplyAnimationState(Animator animator, MsgAnimationState msg)
        {
            try
            {
                if (msg.IsInTransition)
                {
                    // Cross-fade to destination state
                    animator.CrossFade(msg.TransitionDestinationStateHash, 0.1f, 0, msg.NormalizedTime);
                }
                else
                {
                    // Play state directly
                    animator.Play(msg.AnimatorStateHash, 0, msg.NormalizedTime);
                }

                animator.speed = msg.Speed;

                Plugin.Log.LogInfo($"[AnimationSync] Applied animation to remote player {msg.PlayerId}: hash={msg.AnimatorStateHash}, time={msg.NormalizedTime:F2}");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"[AnimationSync] Failed to apply animation to player {msg.PlayerId}: {ex.Message}");
            }
        }

        public static void RegisterRemoteAnimator(int playerId, Animator animator)
        {
            lock (_lock)
            {
                _remoteAnimators[playerId] = animator;
            }
            Plugin.Log.LogInfo($"[AnimationSync] Registered animator for player {playerId}");
        }

        public static void UnregisterRemoteAnimator(int playerId)
        {
            lock (_lock)
            {
                _remoteAnimators.Remove(playerId);
            }
            Plugin.Log.LogInfo($"[AnimationSync] Unregistered animator for player {playerId}");
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _remoteAnimators.Clear();
            }
            Plugin.Log.LogInfo("[AnimationSync] Cleared all remote animators");
        }
    }
}
