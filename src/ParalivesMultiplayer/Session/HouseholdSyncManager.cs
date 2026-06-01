using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class HouseholdSyncManager
    {
        static readonly Dictionary<ulong, object> _remoteCharactersInHousehold = new Dictionary<ulong, object>();
        static readonly object _lock = new object();

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[HouseholdSync] Initialized");
        }

        public static void AddRemoteCharacterToHousehold(ulong characterGuid, object assetCharacter)
        {
            if (assetCharacter == null) return;

            lock (_lock)
            {
                _remoteCharactersInHousehold[characterGuid] = assetCharacter;
            }

            try
            {
                ParalivesGameApiResolver.Resolve();

                var hhMgr = ParalivesGameApiResolver.HouseholdManagerInstance;
                if (hhMgr == null)
                {
                    Plugin.Log.LogWarning("[HouseholdSync] HouseholdManager instance not available");
                    return;
                }

                var hhType = ParalivesGameApiResolver.HouseholdManagerType;
                var currentProp = hhType.GetProperty("CurrentHousehold");
                var current = currentProp?.GetValue(hhMgr);
                if (current == null)
                {
                    Plugin.Log.LogWarning("[HouseholdSync] No current household");
                    return;
                }

                var dataProp = current.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(current);
                if (data == null)
                {
                    Plugin.Log.LogWarning("[HouseholdSync] Current household has no Data");
                    return;
                }

                var charsField = data.GetType().GetField("Characters");
                if (charsField == null)
                {
                    Plugin.Log.LogWarning("[HouseholdSync] Household Data has no Characters field");
                    return;
                }

                var chars = charsField.GetValue(data) as System.Collections.IList;
                if (chars == null)
                {
                    Plugin.Log.LogWarning("[HouseholdSync] Characters list is null");
                    return;
                }

                // Check if already present
                bool alreadyPresent = false;
                foreach (var c in chars)
                {
                    var guidField = c.GetType().GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
                    var guid = guidField != null ? (ulong)guidField.GetValue(c) : 0UL;
                    if (guid == characterGuid)
                    {
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    chars.Add(assetCharacter);
                    Plugin.Log.LogInfo($"[HouseholdSync] Added character GUID={characterGuid:X} to current household. Total={chars.Count}");
                }
                else
                {
                    Plugin.Log.LogInfo($"[HouseholdSync] Character GUID={characterGuid:X} already in household");
                }

                // Also register with CharacterManager so the game treats it as a real character
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                if (charMgr != null)
                {
                    var regMethod = ParalivesGameApiResolver.RegisterCharacterMethod;
                    if (regMethod != null)
                    {
                        try
                        {
                            regMethod.Invoke(charMgr, new object[] { assetCharacter });
                            Plugin.Log.LogInfo($"[HouseholdSync] Registered character GUID={characterGuid:X} with CharacterManager");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"[HouseholdSync] RegisterCharacter failed: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[HouseholdSync] Error adding to household: {ex.Message}");
            }
        }

        public static void RemoveRemoteCharacterFromHousehold(ulong characterGuid)
        {
            lock (_lock)
            {
                _remoteCharactersInHousehold.Remove(characterGuid);
            }

            try
            {
                var hhMgr = ParalivesGameApiResolver.HouseholdManagerInstance;
                if (hhMgr == null) return;

                var hhType = ParalivesGameApiResolver.HouseholdManagerType;
                var currentProp = hhType.GetProperty("CurrentHousehold");
                var current = currentProp?.GetValue(hhMgr);
                if (current == null) return;

                var dataProp = current.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(current);
                if (data == null) return;

                var charsField = data.GetType().GetField("Characters");
                if (charsField == null) return;

                var chars = charsField.GetValue(data) as System.Collections.IList;
                if (chars == null) return;

                object toRemove = null;
                foreach (var c in chars)
                {
                    var guidField = c.GetType().GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
                    var guid = guidField != null ? (ulong)guidField.GetValue(c) : 0UL;
                    if (guid == characterGuid)
                    {
                        toRemove = c;
                        break;
                    }
                }

                if (toRemove != null)
                {
                    chars.Remove(toRemove);
                    Plugin.Log.LogInfo($"[HouseholdSync] Removed character GUID={characterGuid:X} from household. Remaining={chars.Count}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[HouseholdSync] Error removing from household: {ex.Message}");
            }
        }

        public static void RemoveAllRemoteCharactersFromHousehold()
        {
            lock (_lock)
            {
                var guids = new List<ulong>(_remoteCharactersInHousehold.Keys);
                foreach (var g in guids)
                    RemoveRemoteCharacterFromHousehold(g);
                _remoteCharactersInHousehold.Clear();
            }
            Plugin.Log.LogInfo("[HouseholdSync] Removed all remote characters from household (save safety)");
        }

        public static void ClearAll()
        {
            RemoveAllRemoteCharactersFromHousehold();
        }

        public static void TriggerUIRefresh()
        {
            try
            {
                ParalivesGameApiResolver.Resolve();
                var uiCharsType = ParalivesGameApiResolver.ResolveType("UICharacters");
                if (uiCharsType == null) return;

                // Try to find UpdateCharacterList via reflection on instances
                var instances = UnityEngine.Object.FindObjectsOfType(uiCharsType);
                foreach (var inst in instances)
                {
                    if (inst == null) continue;
                    var updateMethod = uiCharsType.GetMethod("UpdateCharacterList",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(inst, null);
                        Plugin.Log.LogInfo("[HouseholdSync] Triggered UICharacters.UpdateCharacterList");
                    }
                    else
                    {
                        // Fallback: try InternalOnShow or Show
                        var showMethod = uiCharsType.GetMethod("InternalOnShow",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (showMethod != null)
                        {
                            showMethod.Invoke(inst, null);
                            Plugin.Log.LogInfo("[HouseholdSync] Triggered UICharacters.InternalOnShow");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[HouseholdSync] UI refresh failed: {ex.Message}");
            }
        }
    }
}
