using System;
using System.Collections.Generic;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Session;
using UnityEngine;

namespace ParalivesMultiplayer.Input
{
    public static class InputRouter
    {
        static readonly Dictionary<int, RemotePlayerInput> _remoteInputs = new Dictionary<int, RemotePlayerInput>();
        static readonly object _lock = new object();

        const float InputDecayRate = 5f;
        const float ButtonHoldTimeout = 0.5f;

        public static event Action<int, MsgInputCommand> OnRemoteInputReceived;

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[InputRouter] Initialized");
        }

        public static void RegisterRemotePlayer(int playerId)
        {
            lock (_lock)
            {
                _remoteInputs[playerId] = new RemotePlayerInput
                {
                    PlayerId = playerId,
                    LastUpdateTime = Time.time,
                    ActiveButtons = new Dictionary<string, float>()
                };
            }
            Plugin.Log.LogInfo($"[InputRouter] Registered remote player {playerId}");
        }

        public static void UnregisterRemotePlayer(int playerId)
        {
            lock (_lock)
            {
                _remoteInputs.Remove(playerId);
            }
            Plugin.Log.LogInfo($"[InputRouter] Unregistered remote player {playerId}");
        }

        public static void ProcessRemoteInput(MsgInputCommand cmd)
        {
            OnRemoteInputReceived?.Invoke(cmd.PlayerId, cmd);

            lock (_lock)
            {
                if (!_remoteInputs.TryGetValue(cmd.PlayerId, out var input)) return;

                input.LastUpdateTime = Time.time;

                if (cmd.IsButton)
                {
                    if (cmd.ValueX > 0f)
                    {
                        input.ActiveButtons[cmd.ButtonName] = Time.time + ButtonHoldTimeout;
                        Plugin.Log.LogDebug($"[InputRouter] Player {cmd.PlayerId} pressed {cmd.ButtonName}");
                    }
                    else
                    {
                        input.ActiveButtons.Remove(cmd.ButtonName);
                    }
                }
                else
                {
                    switch (cmd.Action)
                    {
                        case InputAction.MoveHorizontal:
                            input.MoveX = cmd.ValueX;
                            break;
                        case InputAction.MoveVertical:
                            input.MoveY = cmd.ValueY;
                            break;
                        case InputAction.LookX:
                            input.LookX = cmd.ValueX;
                            break;
                        case InputAction.LookY:
                            input.LookY = cmd.ValueY;
                            break;
                    }
                }
            }
        }

        public static void UpdateDecay()
        {
            float now = Time.time;
            lock (_lock)
            {
                foreach (var kv in _remoteInputs)
                {
                    var input = kv.Value;

                    input.MoveX *= Mathf.Exp(-InputDecayRate * Time.deltaTime);
                    input.MoveY *= Mathf.Exp(-InputDecayRate * Time.deltaTime);
                    input.LookX *= Mathf.Exp(-InputDecayRate * Time.deltaTime);
                    input.LookY *= Mathf.Exp(-InputDecayRate * Time.deltaTime);

                    var toRemove = new List<string>();
                    foreach (var btn in input.ActiveButtons)
                    {
                        if (now > btn.Value)
                            toRemove.Add(btn.Key);
                    }
                    foreach (var name in toRemove)
                        input.ActiveButtons.Remove(name);
                }
            }
        }

        public static bool GetButton(int playerId, string buttonName)
        {
            lock (_lock)
            {
                if (!_remoteInputs.TryGetValue(playerId, out var input)) return false;
                return input.ActiveButtons.ContainsKey(buttonName);
            }
        }

        public static Vector3 GetMovement(int playerId)
        {
            lock (_lock)
            {
                if (!_remoteInputs.TryGetValue(playerId, out var input)) return Vector3.zero;
                return new Vector3(input.MoveX, 0f, input.MoveY);
            }
        }

        public static Vector2 GetLookDelta(int playerId)
        {
            lock (_lock)
            {
                if (!_remoteInputs.TryGetValue(playerId, out var input)) return Vector2.zero;
                return new Vector2(input.LookX, input.LookY);
            }
        }

        public static void ClearAll()
        {
            lock (_lock) _remoteInputs.Clear();
            Plugin.Log.LogInfo("[InputRouter] Cleared all remote inputs");
        }
    }

    class RemotePlayerInput
    {
        public int PlayerId;
        public float LastUpdateTime;
        public float MoveX;
        public float MoveY;
        public float LookX;
        public float LookY;
        public Dictionary<string, float> ActiveButtons;
    }
}
