using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class CharacterSelectionPatches
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                var charMgrType = ParalivesGameApiResolver.CharacterManagerType;
                if (charMgrType == null)
                {
                    PatchLogger.LogWarning("CharacterManager type not resolved, skipping selection patches");
                    return;
                }

                var selectMethod = AccessTools.Method(charMgrType, "SelectCharacter");
                if (selectMethod == null)
                {
                    PatchLogger.LogWarning("CharacterManager.SelectCharacter not found");
                    return;
                }

                PatchLogger.SafePatch(harmony, selectMethod,
                    new HarmonyMethod(typeof(CharacterSelectionPatches), nameof(OnSelectCharacterPostfix)),
                    "CharacterManager.SelectCharacter");

                PatchLogger.Log("[Selection] Patched CharacterManager.SelectCharacter");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to patch CharacterManager.SelectCharacter: {ex.Message}");
            }
        }

        static void OnSelectCharacterPostfix(ulong characterGUID, int playerIndex, bool force)
        {
            if (!MultiplayerSession.IsActive) return;
            if (playerIndex != 0) return; // Only sync local player (index 0)

            var msg = new Networking.Messages.MsgSelectCharacter
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                CharacterGuid = characterGUID,
                Selected = true
            };

            var net = UdpNetworkManager.Instance;
            if (net == null) return;

            if (MultiplayerSession.IsHost)
                net.SendToAllClients(msg);
            else
                net.SendToHost(msg);

            PatchLogger.LogDebug($"[Selection] Broadcast select: player={MultiplayerSession.LocalPlayerId}, char={characterGUID:X}");
        }
    }
}
