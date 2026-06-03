using System;
using System.Collections.Generic;
using System.Reflection;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class InteractionSyncManager
    {
        static readonly Dictionary<ulong, List<object>> _pendingInteractions = new Dictionary<ulong, List<object>>();
        static readonly object _lock = new object();

        public static event Action<int, ulong, ulong> OnRemoteInteractionRequested;

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[InteractionSync] Initialized");
        }

        public static void RequestInteraction(ulong requesterGuid, ulong targetGuid, ulong interactionGuid, bool isSocial, bool isAutonomous)
        {
            if (!MultiplayerSession.IsActive) return;

            var msg = new MsgInteractionRequest
            {
                RequesterPlayerId = MultiplayerSession.LocalPlayerId,
                RequesterCharacterGuid = requesterGuid,
                TargetCharacterGuid = targetGuid,
                InteractionGuid = interactionGuid,
                IsSocial = isSocial,
                IsAutonomous = isAutonomous
            };

            var net = UdpNetworkManager.Instance;
            if (net == null) return;

            if (MultiplayerSession.IsHost)
                net.SendToAllExcept(0, msg);
            else
                net.SendToHost(msg);

            Plugin.Log.LogInfo($"[InteractionSync] Local request: req={requesterGuid:X}, target={targetGuid:X}, interaction={interactionGuid:X}");
        }

        public static void ProcessRemoteInteractionRequest(MsgInteractionRequest msg)
        {
            Plugin.Log.LogInfo($"[InteractionSync] Remote request from player {msg.RequesterPlayerId}: req={msg.RequesterCharacterGuid:X}, target={msg.TargetCharacterGuid:X}, interaction={msg.InteractionGuid:X}");

            OnRemoteInteractionRequested?.Invoke(msg.RequesterPlayerId, msg.TargetCharacterGuid, msg.InteractionGuid);

            // If this client owns the target character, inject the interaction
            if (CharacterOwnershipManager.IsOwnedByLocalPlayer(msg.TargetCharacterGuid))
            {
                InjectInteractionIntoLocalCharacter(msg.TargetCharacterGuid, msg.RequesterCharacterGuid, msg.InteractionGuid, msg.IsSocial);
            }
            else if (MultiplayerSession.IsHost)
            {
                // Host rebroadcasts to ensure all clients see it
                var net = UdpNetworkManager.Instance;
                if (net != null)
                    net.SendToAllExcept(msg.SenderClientId, msg);
            }
        }

        static void InjectInteractionIntoLocalCharacter(ulong targetGuid, ulong requesterGuid, ulong interactionGuid, bool isSocial)
        {
            try
            {
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                if (charMgr == null) return;

                var getByGuid = ParalivesGameApiResolver.GetCharacterByGUIDMethod;
                if (getByGuid == null) return;

                var targetChar = getByGuid.Invoke(charMgr, new object[] { targetGuid });
                if (targetChar == null)
                {
                    Plugin.Log.LogWarning($"[InteractionSync] Target character {targetGuid:X} not found for injection");
                    return;
                }

                var dataProp = targetChar.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(targetChar);
                if (data == null) return;

                var interactionsField = data.GetType().GetField("CurrentInteractionsInQueue");
                if (interactionsField == null) return;

                var interactions = interactionsField.GetValue(data) as System.Collections.IList;
                if (interactions == null) return;

                var interactionDataType = FindTypeInAssemblies("AssetCharacterDataInteraction");
                if (interactionDataType == null)
                {
                    Plugin.Log.LogWarning("[InteractionSync] AssetCharacterDataInteraction type not found");
                    return;
                }

                object newInteraction = CreateInteractionInstance(interactionDataType, interactions, interactionGuid, requesterGuid, isSocial);
                if (newInteraction == null)
                {
                    Plugin.Log.LogError("[InteractionSync] All interaction creation strategies failed");
                    return;
                }

                interactions.Add(newInteraction);
                Plugin.Log.LogInfo($"[InteractionSync] Injected interaction {interactionGuid:X} into local character {targetGuid:X} (queue now has {interactions.Count} items)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[InteractionSync] Failed to inject interaction: {ex.Message}");
            }
        }

        static object CreateInteractionInstance(Type interactionDataType, System.Collections.IList existingInteractions, ulong interactionGuid, ulong requesterGuid, bool isSocial)
        {
            object newInteraction = null;

            // Strategy 1: Parameterless constructor
            try
            {
                newInteraction = System.Activator.CreateInstance(interactionDataType, true);
            }
            catch (Exception ex1)
            {
                Plugin.Log.LogDebug($"[InteractionSync] Strategy 1 (Activator) failed: {ex1.Message}");
            }

            // Strategy 2: Clone from an existing interaction in the queue
            if (newInteraction == null && existingInteractions.Count > 0)
            {
                try
                {
                    var prototype = existingInteractions[0];
                    if (prototype != null)
                    {
                        var cloneMethod = prototype.GetType().GetMethod("MemberwiseClone",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (cloneMethod != null)
                        {
                            newInteraction = cloneMethod.Invoke(prototype, null);
                            Plugin.Log.LogDebug("[InteractionSync] Strategy 2 (clone) succeeded");
                        }
                    }
                }
                catch (Exception ex2)
                {
                    Plugin.Log.LogDebug($"[InteractionSync] Strategy 2 (clone) failed: {ex2.Message}");
                }
            }

            // Strategy 3: Uninitialized object (bypasses constructors entirely)
            if (newInteraction == null)
            {
                try
                {
                    newInteraction = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(interactionDataType);
                    Plugin.Log.LogDebug("[InteractionSync] Strategy 3 (uninitialized object) succeeded");
                }
                catch (Exception ex3)
                {
                    Plugin.Log.LogDebug($"[InteractionSync] Strategy 3 (uninitialized) failed: {ex3.Message}");
                }
            }

            if (newInteraction == null) return null;

            // Populate fields
            try
            {
                var guidField = interactionDataType.GetField("GUID");
                if (guidField != null) guidField.SetValue(newInteraction, interactionGuid);

                var otherField = interactionDataType.GetField("OtherCharacterGUID");
                if (otherField != null && isSocial)
                    otherField.SetValue(newInteraction, requesterGuid);

                // Set a default state so the interaction is valid
                var stateField = interactionDataType.GetField("State");
                if (stateField != null)
                {
                    var stateType = stateField.FieldType;
                    if (stateType.IsEnum)
                    {
                        var values = System.Enum.GetValues(stateType);
                        if (values.Length > 0)
                            stateField.SetValue(newInteraction, values.GetValue(0));
                    }
                }

                var priorityField = interactionDataType.GetField("Priority");
                if (priorityField != null && priorityField.FieldType == typeof(int))
                    priorityField.SetValue(newInteraction, 0);

                var isSocialField = interactionDataType.GetField("IsSocial");
                if (isSocialField != null && isSocialField.FieldType == typeof(bool))
                    isSocialField.SetValue(newInteraction, isSocial);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[InteractionSync] Field population error: {ex.Message}");
            }

            return newInteraction;
        }

        static Type FindTypeInAssemblies(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(name);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                _pendingInteractions.Clear();
            }
            Plugin.Log.LogInfo("[InteractionSync] Cleared all pending interactions");
        }
    }

}
