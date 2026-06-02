using System;
using System.Collections.Generic;
using System.Reflection;
using ParalivesMultiplayer.Networking;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public class RemoteCharacterEntry
    {
        public int PlayerId;
        public ulong CharacterGuid;
        public object GameNativeCharacter;
        public Transform ControlledTransform;
        public GameObject FallbackProxy;
        public bool IsGameNative;
        public Vector3 LastKnownPosition;
        public Quaternion LastKnownRotation;
    }

    public static class RemoteCharacterManager
    {
        static readonly Dictionary<int, RemoteCharacterEntry> _remoteCharacters = new Dictionary<int, RemoteCharacterEntry>();
        static readonly object _lock = new object();

        static Transform _localCharacterTransform;
        static bool _localCharacterFound;
        static float _lastFindAttemptTime;
        const float FindRetryInterval = 1f; // seconds between retry attempts

        public static Transform LocalCharacterTransform => _localCharacterTransform;
        public static bool HasLocalCharacter => _localCharacterFound;

        public static event Action<int, RemoteCharacterEntry> OnRemoteCharacterCreated;
        public static event Action<int> OnRemoteCharacterRemoved;

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[Paramulti] Initializing RemoteCharacterManager");
            ParalivesGameApiResolver.Resolve();
            FindLocalCharacter();
        }

        static ulong GetCharacterGuid(object c)
        {
            var t = c.GetType();
            var guidProp = t.GetProperty("GUID", BindingFlags.Public | BindingFlags.Instance);
            ulong guid = guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;
            if (guid == 0)
            {
                var f = t.GetField("m_CharacterGUID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) guid = (ulong)f.GetValue(c);
            }
            if (guid == 0)
            {
                var f2 = t.BaseType?.GetField("m_GUID", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f2 != null) guid = (ulong)f2.GetValue(c);
            }
            return guid;
        }

        public static void FindLocalCharacter()
        {
            ParalivesGameApiResolver.Resolve();

            if (ParalivesGameApiResolver.CharacterManagerInstance == null) return;

            if (Time.time - _lastFindAttemptTime < FindRetryInterval) return;
            _lastFindAttemptTime = Time.time;

            try
            {
                Plugin.Log.LogInfo("[Paramulti] FindLocalCharacter Path1: PlayerManagerInstance check");
                // Path 1: PlayerManager HybridPlayer.SelectedCharactersGUID (list of active controlled characters)
                if (ParalivesGameApiResolver.PlayerManagerInstance != null &&
                    ParalivesGameApiResolver.GetHybridPlayerMethod != null)
                {
                    Plugin.Log.LogInfo("[Paramulti] FindLocalCharacter Path1: getting HybridPlayer...");
                    var pm = ParalivesGameApiResolver.PlayerManagerInstance;
                    var player0 = ParalivesGameApiResolver.GetHybridPlayerMethod.Invoke(pm, new object[] { 0 });
                    if (player0 != null)
                    {
                        var playerProp = player0.GetType().GetProperty("Player");
                        if (playerProp != null)
                        {
                            var playerObj = playerProp.GetValue(player0);
                            if (playerObj != null)
                            {
                                var selectedCharsField = playerObj.GetType().GetField("SelectedCharactersGUID",
                                    BindingFlags.Public | BindingFlags.Instance);
                                Plugin.Log.LogInfo($"[Paramulti] FindLocalCharacter Path1: SelectedCharsField={selectedCharsField != null}");
                                if (selectedCharsField != null)
                                {
                                    var selectedList = selectedCharsField.GetValue(playerObj) as System.Collections.IList;
                                    Plugin.Log.LogInfo($"[Paramulti] FindLocalCharacter Path1: SelectedList count={selectedList?.Count ?? -1}");
                                    if (selectedList != null && selectedList.Count > 0)
                                    {
                                        var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                                        var loadedVisMethod = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                                        foreach (var selectedGuid in selectedList)
                                        {
                                            var guid = Convert.ToUInt64(selectedGuid);
                                            Plugin.Log.LogInfo($"[Paramulti] Path1: trying selected guid={guid:X}");
                                            if (guid == 0) continue;
                                            var runtimeVisual = loadedVisMethod.Invoke(charMgr, new object[] { guid });
                                            Plugin.Log.LogInfo($"[Paramulti] Path1: GetLoadedCharacterVisual({guid:X})={(runtimeVisual != null ? runtimeVisual.ToString() : "null")}");
                                            if (runtimeVisual != null)
                                            {
                                                var t = ExtractTransform(runtimeVisual);
                                                Plugin.Log.LogInfo($"[Paramulti] Path1: transform t={t?.gameObject?.name ?? "null"}, valid={t != null && IsValidLocalTransform(t)}");
                                                if (t != null && IsValidLocalTransform(t))
                                                {
                                                    _localCharacterTransform = t;
                                                    _localCharacterFound = true;
                                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via SelectedCharactersGUID + GetLoadedCharacterVisual={guid:X}: {t.gameObject.name}");
                                                    return;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Path 2: HouseholdManager — find first household member with a loaded visual
                if (ParalivesGameApiResolver.HouseholdManagerInstance != null &&
                    ParalivesGameApiResolver.GetCharactersInCurrentHouseholdMethod != null &&
                    ParalivesGameApiResolver.GetLoadedCharacterVisualMethod != null)
                {
                    var hm = ParalivesGameApiResolver.HouseholdManagerInstance;
                    var chars = ParalivesGameApiResolver.GetCharactersInCurrentHouseholdMethod.Invoke(hm, null) as System.Collections.IList;
                    Plugin.Log.LogInfo($"[Paramulti] FindLocalCharacter Path2: HouseholdManager chars count={chars?.Count ?? -1}");
                    if (chars != null && chars.Count > 0)
                    {
                        var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                        var loadedVisMethod = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                        foreach (var c in chars)
                        {
                            var guid = GetCharacterGuid(c);
                            if (guid == 0) continue;

                            var runtimeVisual = loadedVisMethod.Invoke(charMgr, new object[] { guid });
                            Plugin.Log.LogInfo($"[Paramulti] FindLocalCharacter Path2: household member GUID={guid:X}, runtimeVisual={runtimeVisual?.GetType().Name ?? "null"}");
                            if (runtimeVisual != null)
                            {
                                var t = ExtractTransform(runtimeVisual);
                                Plugin.Log.LogInfo($"[Paramulti] FindLocalCharacter Path2: ExtractTransform result={t?.name ?? "null"}, pos={t?.position}");
                                if (t != null && IsValidLocalTransform(t))
                                {
                                    _localCharacterTransform = t;
                                    _localCharacterFound = true;
                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via HouseholdManager GUID={guid:X}: {t.gameObject.name}");
                                    return;
                                }
                                else
                                {
                                    Plugin.Log.LogWarning($"[Paramulti] FindLocalCharacter Path2: ExtractTransform null or IsValidLocalTransform failed for GUID={guid:X}");
                                }
                            }
                        }
                    }
                }

                // Path 3: Camera follow target — fallback, must be in CharacterManager list
                var mainCam = UnityEngine.Camera.main;
                if (mainCam != null)
                {
                    var camController = mainCam.GetComponent("CameraController");
                    if (camController != null)
                    {
                        var followField = camController.GetType().GetField("Target", BindingFlags.Public | BindingFlags.Instance);
                        if (followField == null)
                            followField = camController.GetType().GetField("FollowTarget", BindingFlags.Public | BindingFlags.Instance);
                        if (followField == null)
                            followField = camController.GetType().GetField("CurrentTarget", BindingFlags.Public | BindingFlags.Instance);
                        if (followField == null)
                            followField = camController.GetType().GetField("m_FollowTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (followField == null)
                            followField = camController.GetType().GetField("_followTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (followField == null)
                            followField = camController.GetType().GetField("m_Target", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (followField != null)
                        {
                            var followObj = followField.GetValue(camController);
                            Transform ft = null;
                            if (followObj is Transform t) ft = t;
                            else if (followObj is GameObject g) ft = g.transform;

                            if (ft != null && IsValidLocalTransform(ft) && IsTransformInCharacterManager(ft))
                            {
                                _localCharacterTransform = ft;
                                _localCharacterFound = true;
                                Plugin.Log.LogInfo($"[Paramulti] Found local character via camera follow target: {ft.gameObject.name}");
                                return;
                            }
                        }
                    }
                }

                // Path 4: Scene scan — find any active CharacterVisual not in void space
                var allVisuals = UnityEngine.Object.FindObjectsOfType<Component>();
                foreach (var comp in allVisuals)
                {
                    if (comp == null) continue;
                    if (comp.GetType().Name == "CharacterVisual")
                    {
                        var t = ExtractTransform(comp);
                        if (t != null && IsValidLocalTransform(t))
                        {
                            _localCharacterTransform = t;
                            _localCharacterFound = true;
                            Plugin.Log.LogInfo($"[Paramulti] Found local character via scene scan: {t.gameObject.name} at {t.position}");
                            return;
                        }
                    }
                }

                // Path 5: Fallback — CharacterVisualInEdition (Character Creator only)
                if (ParalivesGameApiResolver.PlayerManagerInstance != null &&
                    ParalivesGameApiResolver.GetHybridPlayerMethod != null)
                {
                    var pm = ParalivesGameApiResolver.PlayerManagerInstance;
                    var player0 = ParalivesGameApiResolver.GetHybridPlayerMethod.Invoke(pm, new object[] { 0 });
                    if (player0 != null)
                    {
                        var cveField = player0.GetType().GetField("CharacterVisualInEdition",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (cveField != null)
                        {
                            var cve = cveField.GetValue(player0);
                            if (cve != null)
                            {
                                var t = ExtractTransform(cve);
                                if (t != null)
                                {
                                    _localCharacterTransform = t;
                                    _localCharacterFound = true;
                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via CharacterVisualInEdition fallback: {t.gameObject.name}");
                                    return;
                                }
                            }
                        }
                    }
                }

                Plugin.Log.LogWarning("[Paramulti] Could not find local character through any path");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] Error finding local character: {ex.Message}");
            }
        }

        static bool IsTransformInCharacterManager(Transform t)
        {
            if (ParalivesGameApiResolver.CharacterManagerInstance == null) return false;
            var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
            var cmType = ParalivesGameApiResolver.CharacterManagerType;
            var charsProp = cmType.GetProperty("Characters");
            if (charsProp == null) return false;
            var chars = charsProp.GetValue(charMgr) as System.Collections.IList;
            if (chars == null) return false;
            foreach (var c in chars)
            {
                var visualProp = c.GetType().GetProperty("Visual");
                var visual = visualProp?.GetValue(c);
                var vt = ExtractTransform(visual);
                if (vt == t) return true;
            }
            return false;
        }

        static bool IsValidLocalTransform(Transform t)
        {
            if (t == null) return false;
            var name = t.gameObject.name;
            // Reject Character Creator visual
            if (name.IndexOf("CharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("CreatorVisual", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            // Reject void / character-creator / photo-mode coordinate space.
            // Ground in Paralives is around Y≈0..10; anything below Y=-1 is below the world.
            if (t.position.y < -1f) return false;
            if (t.position.x > 500f || t.position.x < -500f) return false;
            if (t.position.y > 500f || t.position.y < -500f) return false;
            if (t.position.z > 500f || t.position.z < -500f) return false;
            return true;
        }

        // A position is in-world if it's not in the character-creator/photo-mode void coordinate space.
        static bool IsValidWorldPosition(UnityEngine.Vector3 p)
        {
            if (p.y < -1f) return false;
            if (p.x > 500f || p.x < -500f) return false;
            if (p.y > 500f) return false;
            if (p.z > 500f || p.z < -500f) return false;
            return true;
        }

        public static void CreateRemoteCharacter(int playerId, string playerName)
        {
            lock (_lock)
            {
                if (_remoteCharacters.ContainsKey(playerId))
                {
                    Plugin.Log.LogWarning($"[Paramulti] Character for player {playerId} already exists");
                    return;
                }
            }

            Vector3 spawnPos = ComputeSpawnPosition();

            var entry = TryCreateGameNativeCharacter(playerId, playerName, spawnPos);

            if (entry == null)
            {
                Plugin.Log.LogWarning($"[Paramulti] Game-native spawn failed for player {playerId}, trying prefab fallback");
                entry = TryCreatePrefabClone(playerId, playerName, spawnPos);
            }

            if (entry == null)
            {
                Plugin.Log.LogWarning($"[Paramulti] Prefab clone failed for player {playerId}, using basic fallback proxy");
                entry = CreateFallbackProxy(playerId, playerName, spawnPos);
            }

            if (entry == null)
            {
                Plugin.Log.LogError($"[Paramulti] CRITICAL: All spawn methods failed for player {playerId}");
                return;
            }

            lock (_lock)
            {
                _remoteCharacters[playerId] = entry;
            }

            OnRemoteCharacterCreated?.Invoke(playerId, entry);
            Plugin.Log.LogInfo($"[Paramulti][ProxyManager] Spawning proxy GameObject for Player {playerId} (Fallback: {!entry.IsGameNative}). gameNative={entry.IsGameNative}, spawnPos={spawnPos}");
        }

        static Vector3 ComputeSpawnPosition()
        {
            if (_localCharacterTransform != null)
            {
                var basePos = _localCharacterTransform.position;
                return new Vector3(basePos.x + 2f, basePos.y, basePos.z + 2f);
            }
            return new Vector3(0f, 0f, 0f);
        }

        public static RemoteCharacterEntry CreateTestProxy(int playerId, string playerName, Vector3 spawnPos)
        {
            Plugin.Log.LogInfo($"[Paramulti] Creating TEST proxy for player {playerId} at {spawnPos}");
            var entry = CreateFallbackProxy(playerId, playerName, spawnPos);
            if (entry != null)
            {
                lock (_lock)
                {
                    _remoteCharacters[playerId] = entry;
                }
                OnRemoteCharacterCreated?.Invoke(playerId, entry);
            }
            return entry;
        }

        static ulong GetLocalCharacterModelGuid()
        {
            if (_localCharacterTransform == null) return 0;

            try
            {
                // Get CharacterGUID from CharacterVisual component
                ulong characterGuid = 0;
                foreach (var comp in _localCharacterTransform.GetComponents<Component>())
                {
                    if (comp == null || comp.GetType().Name != "CharacterVisual") continue;
                    var guidProp = comp.GetType().GetProperty("CharacterGUID");
                    if (guidProp != null)
                    {
                        characterGuid = (ulong)guidProp.GetValue(comp);
                        break;
                    }
                }

                if (characterGuid == 0) return 0;

                // Use GetCharacterByGUID to find the AssetCharacter
                var getCharMethod = ParalivesGameApiResolver.GetCharacterByGUIDMethod;
                if (getCharMethod == null || ParalivesGameApiResolver.CharacterManagerInstance == null)
                    return 0;

                var assetChar = getCharMethod.Invoke(ParalivesGameApiResolver.CharacterManagerInstance, new object[] { characterGuid });
                if (assetChar == null) return 0;

                // Get Data.CurrentCharacterModelGUID
                var dataProp = assetChar.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(assetChar);
                if (data == null) return 0;

                var modelField = data.GetType().GetField("CurrentCharacterModelGUID");
                if (modelField == null)
                {
                    // Try alternative field names
                    modelField = data.GetType().GetField("CharacterModelGUID");
                    if (modelField == null) return 0;
                }

                var modelGuid = (ulong)modelField.GetValue(data);
                return modelGuid;
            }
            catch
            {
                return 0;
            }
        }

        // GUID is a public *field* on AssetData (not a property), so reflection must use GetField.
        static ulong GetAssetGuid(object asset)
        {
            if (asset == null) return 0UL;
            var f = asset.GetType().GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return 0UL;
            try { return (ulong)f.GetValue(asset); }
            catch { return 0UL; }
        }

        static bool SetAssetGuid(object asset, ulong guid)
        {
            if (asset == null) return false;
            var f = asset.GetType().GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return false;
            try { f.SetValue(asset, guid); return true; }
            catch { return false; }
        }

        // Set any public field on an asset (IsInHousehold, IsVisibleInWorld, DoNotLoadVisual, etc).
        // Walks the base-type chain so we can hit private fields too if needed.
        static bool SetAssetField(object asset, string name, object value)
        {
            if (asset == null) return false;
            var t = asset.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    try { f.SetValue(asset, value); return true; }
                    catch { return false; }
                }
                t = t.BaseType;
            }
            return false;
        }

        // LoadCharacterVisual only works for GUIDs present in AssetManager._assets. Inject our clone.
        static bool InjectIntoAssetManagerAssets(ulong guid, object asset)
        {
            var am = ParalivesGameApiResolver.AssetManagerInstance;
            if (am == null || asset == null) return false;
            var f = am.GetType().GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            var assets = f.GetValue(am) as System.Collections.IDictionary;
            if (assets == null) return false;
            try { assets[guid] = asset; return true; }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] InjectIntoAssetManagerAssets({guid:X}) failed: {ex.Message}");
                return false;
            }
        }

        static bool RemoveFromAssetManagerAssets(ulong guid)
        {
            var am = ParalivesGameApiResolver.AssetManagerInstance;
            if (am == null) return false;
            var f = am.GetType().GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            var assets = f.GetValue(am) as System.Collections.IDictionary;
            if (assets == null || !assets.Contains(guid)) return false;
            try { assets.Remove(guid); return true; }
            catch { return false; }
        }

        static bool AssetManagerHasAsset(ulong guid)
        {
            var am = ParalivesGameApiResolver.AssetManagerInstance;
            if (am == null) return false;
            var f = am.GetType().GetField("_assets", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            var assets = f.GetValue(am) as System.Collections.IDictionary;
            return assets != null && assets.Contains(guid);
        }

        // PremadeType.No is enum value 0. RegisterCharacter is a no-op for non-No premade types.
        static bool ForcePremadeTypeNo(object assetCharacter)
        {
            if (assetCharacter == null) return false;
            var p = assetCharacter.GetType().GetProperty("PremadeType");
            if (p == null) return false;
            try { p.SetValue(assetCharacter, Enum.ToObject(p.PropertyType, 0)); return true; }
            catch { return false; }
        }

        static bool RemoveFromLoadedCharacterVisuals(ulong guid)
        {
            var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
            if (charMgr == null) return false;
            var f = charMgr.GetType().GetField("LoadedCharacterVisuals", BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return false;
            var dict = f.GetValue(charMgr) as System.Collections.IDictionary;
            if (dict == null || !dict.Contains(guid)) return false;
            try { dict.Remove(guid); return true; }
            catch { return false; }
        }

        static bool RemoveFromCharacterManagerList(object assetChar)
        {
            var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
            if (charMgr == null || assetChar == null) return false;
            var list = charMgr.GetType().GetProperty("Characters")?.GetValue(charMgr) as System.Collections.IList;
            if (list == null || !list.Contains(assetChar)) return false;
            try { list.Remove(assetChar); return true; }
            catch { return false; }
        }

        static object TryFindAnyLocalAssetCharacter()
        {
            var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
            var list = charMgr?.GetType().GetProperty("Characters")?.GetValue(charMgr) as System.Collections.IList;
            if (list == null || list.Count == 0) return null;
            return list[0];
        }

        // Resolve the AssetCharacter the local player is actually controlling.
        // Prefer Player.CameraCurrentCharacterFollowTarget (the in-world sim the camera tracks)
        // since SelectedCharactersGUID[0] can also point at the character-creator/photo-mode copy.
        static object TryGetSelectedAssetCharacter()
        {
            try
            {
                var pm = ParalivesGameApiResolver.PlayerManagerInstance;
                var getHybrid = ParalivesGameApiResolver.GetHybridPlayerMethod;
                if (pm == null || getHybrid == null) return null;

                var hybrid = getHybrid.Invoke(pm, new object[] { 0 });
                if (hybrid == null) return null;

                var playerProp = hybrid.GetType().GetProperty("Player");
                var player = playerProp?.GetValue(hybrid);
                if (player == null) return null;

                ulong guid = 0;

                // 1) Live-world follow target (best signal).
                var followField = player.GetType().GetField("CameraCurrentCharacterFollowTarget",
                    BindingFlags.Public | BindingFlags.Instance);
                if (followField != null)
                {
                    try { guid = (ulong)followField.GetValue(player); } catch { }
                }

                // 2) Fall back to GetSelectedCharacterGUID() (creator/multi-select head).
                if (guid == 0)
                {
                    var method = player.GetType().GetMethod("GetSelectedCharacterGUID",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (method != null)
                    {
                        try { guid = (ulong)method.Invoke(player, null); } catch { }
                    }
                }

                if (guid == 0) return null;

                var getChar = ParalivesGameApiResolver.GetCharacterByGUIDMethod;
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                if (getChar == null || charMgr == null) return null;
                return getChar.Invoke(charMgr, new object[] { guid });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] TryGetSelectedAssetCharacter failed: {ex.Message}");
                return null;
            }
        }

        // Walk localCharacterTransform → CharacterVisual.CharacterGUID → AssetCharacter.
        // Works even when ExtractTransform comparison fails (camera-follow case).
        static object TryGetLocalAssetCharacter()
        {
            if (_localCharacterTransform == null) return null;
            try
            {
                ulong characterGuid = 0;
                foreach (var comp in _localCharacterTransform.GetComponents<Component>())
                {
                    if (comp == null || comp.GetType().Name != "CharacterVisual") continue;
                    var guidField = comp.GetType().GetField("CharacterGUID", BindingFlags.Public | BindingFlags.Instance);
                    if (guidField != null)
                    {
                        characterGuid = (ulong)guidField.GetValue(comp);
                        break;
                    }
                    var guidProp = comp.GetType().GetProperty("CharacterGUID");
                    if (guidProp != null)
                    {
                        characterGuid = (ulong)guidProp.GetValue(comp);
                        break;
                    }
                }
                if (characterGuid == 0) return null;

                var getCharMethod = ParalivesGameApiResolver.GetCharacterByGUIDMethod;
                if (getCharMethod == null || ParalivesGameApiResolver.CharacterManagerInstance == null)
                    return null;
                return getCharMethod.Invoke(ParalivesGameApiResolver.CharacterManagerInstance, new object[] { characterGuid });
            }
            catch { return null; }
        }

        static string TrySerializeLocalCharacterVisualJson()
        {
            try
            {
                // Prefer the actively-controlled sim (Player.GetSelectedCharacterGUID).
                var selected = TryGetSelectedAssetCharacter();
                if (selected != null)
                {
                    var json = TrySerializeVisualJsonFromCharacter(selected);
                    if (!string.IsNullOrEmpty(json)) return json;
                }
                // Fall back to whatever the camera transform resolved to.
                var local = TryGetLocalAssetCharacter();
                if (local == null) return "";
                return TrySerializeVisualJsonFromCharacter(local);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] TrySerializeLocalCharacterVisualJson failed: {ex.Message}");
                return "";
            }
        }

        static string TrySerializeVisualJsonFromCharacter(object assetCharacter)
        {
            try
            {
                if (assetCharacter == null) return "";
                var visualProp = assetCharacter.GetType().GetProperty("Visual");
                var visual = visualProp?.GetValue(assetCharacter);
                if (visual == null) return "";
                var dataProp = visual.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(visual);
                if (data == null) return "";
                return JsonUtility.ToJson(data, false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] TrySerializeVisualJsonFromCharacter failed: {ex.Message}");
                return "";
            }
        }

        // Build a per-clone AssetCharacterVisual whose Data comes from the remote player's JSON.
        // Assigning via the Visual setter writes to _fakePhotoModeVisual, so the getter returns ours
        // and the donor's on-disk visual is left untouched.
        static bool TryApplyRemoteVisualToClone(object clonedChar, object donorChar, string visualJson)
        {
            if (clonedChar == null || string.IsNullOrEmpty(visualJson)) return false;
            try
            {
                // Source the visual shell from the donor so type and runtime members are correct.
                var donorVisualProp = donorChar?.GetType().GetProperty("Visual");
                var donorVisual = donorVisualProp?.GetValue(donorChar);
                if (donorVisual == null) return false;

                var visualCloneMethod = donorVisual.GetType().GetMethod("MemberwiseClone",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var clonedVisual = visualCloneMethod?.Invoke(donorVisual, null);
                if (clonedVisual == null) return false;

                // Replace Data with deserialized remote data.
                var dataProp = clonedVisual.GetType().GetProperty("Data");
                if (dataProp == null) return false;
                var dataType = dataProp.PropertyType;
                var remoteData = JsonUtility.FromJson(visualJson, dataType);
                if (remoteData == null) return false;
                dataProp.SetValue(clonedVisual, remoteData);

                // Mark every Dirty flag so the game recomputes meshes/textures/equipment on render.
                foreach (var name in new[] { "IsTextureDirty", "IsAutoEquipedDirty",
                                             "IsMeshCompositionDirty", "AreBoneDeformationsDirty",
                                             "IsStandaloneItemsDirty", "IsStandaloneItemsDeformationDirty" })
                {
                    try { remoteData.GetType().GetProperty(name)?.SetValue(remoteData, true); } catch { }
                    try
                    {
                        var f = remoteData.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        if (f != null && f.FieldType == typeof(bool)) f.SetValue(remoteData, true);
                    }
                    catch { }
                }

                // Assign to the clone (setter targets _fakePhotoModeVisual, bypassing disk reload).
                var cloneVisualProp = clonedChar.GetType().GetProperty("Visual");
                if (cloneVisualProp == null || !cloneVisualProp.CanWrite) return false;
                cloneVisualProp.SetValue(clonedChar, clonedVisual);

                // Defense: the AssetCharacter class also reads _assetCharacterVisual directly in
                // its own internals (mesh composition, equipment). The Visual setter only writes
                // _fakePhotoModeVisual, so we additionally overwrite the private backing field +
                // mark it as loaded so the getter never tries to reload the donor's visual from disk.
                var cloneType = clonedChar.GetType();
                Type t = cloneType;
                while (t != null)
                {
                    var f = t.GetField("_assetCharacterVisual", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { try { f.SetValue(clonedChar, clonedVisual); } catch { } break; }
                    t = t.BaseType;
                }
                t = cloneType;
                while (t != null)
                {
                    var f = t.GetField("_isCharacterVisualLoaded", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) { try { f.SetValue(clonedChar, true); } catch { } break; }
                    t = t.BaseType;
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] TryApplyRemoteVisualToClone failed: {ex.Message}");
                return false;
            }
        }

        // After MemberwiseClone, clonedChar.Data is the *same reference* as donor.Data.
        // The game runs autonomy/needs on every Characters list entry; both donor and clone
        // would mutate the same AssetCharacterData, depleting the donor's stats 2x and killing
        // the local sim. AssetCharacterData has no Copy() method, so JSON-round-trip it for a
        // fully independent instance.
        static bool TryIsolateClonedCharacterData(object clonedChar)
        {
            if (clonedChar == null) return false;
            try
            {
                var dataProp = clonedChar.GetType().GetProperty("Data");
                if (dataProp == null || !dataProp.CanWrite) return false;
                var data = dataProp.GetValue(clonedChar);
                if (data == null) return false;

                var json = JsonUtility.ToJson(data, false);
                if (string.IsNullOrEmpty(json)) return false;
                var copied = JsonUtility.FromJson(json, data.GetType());
                if (copied == null) return false;

                dataProp.SetValue(clonedChar, copied);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] TryIsolateClonedCharacterData failed: {ex.Message}");
                return false;
            }
        }

        static RemoteCharacterEntry TryCreateGameNativeCharacter(int playerId, string playerName, Vector3 spawnPos)
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] CharacterManager instance not available for game-native spawn");
                    return null;
                }

                // Use AssetManager.GetCharacter + MemberwiseClone instead of CreateCharacterByModelGUID
                // (CreateCharacterByModelGUID crashes with AssetManager.CreateNewAssetPackage NRE)
                if (ParalivesGameApiResolver.AssetManagerInstance == null ||
                    ParalivesGameApiResolver.GetCharacterMethod == null ||
                    ParalivesGameApiResolver.LoadCharacterVisualMethod == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] AssetManager/GetCharacter/LoadCharacterVisual not resolved for game-native spawn");
                    return null;
                }

                ulong modelGuid = GetLocalCharacterModelGuid();
                if (modelGuid == 0)
                {
                    Plugin.Log.LogWarning("[Paramulti] No local character model GUID; cannot create game-native character");
                    return null;
                }

                Plugin.Log.LogInfo($"[Paramulti] Creating game-native character for player {playerId} with model GUID={modelGuid:X}");

                var am = ParalivesGameApiResolver.AssetManagerInstance;
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var assetChar = ParalivesGameApiResolver.GetCharacterMethod.Invoke(am, new object[] { modelGuid });
                if (assetChar == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] AssetManager.GetCharacter returned null");
                    return null;
                }

                var cloneMethod = assetChar.GetType().GetMethod("MemberwiseClone",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (cloneMethod == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] MemberwiseClone not found on asset char type");
                    return null;
                }

                var clonedChar = cloneMethod.Invoke(assetChar, null);
                if (clonedChar == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] MemberwiseClone returned null");
                    return null;
                }

                // Generate a unique GUID for this remote character
                var guid = GenerateGuidForPlayer(playerId);
                SetAssetGuid(clonedChar, guid);

                // Set name
                var dataProp = clonedChar.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(clonedChar);
                if (data != null)
                {
                    var firstNameField = data.GetType().GetField("FirstName");
                    firstNameField?.SetValue(data, playerName);
                    var fullNameField = data.GetType().GetField("FullName");
                    fullNameField?.SetValue(data, playerName);
                }

                // Register
                var regMethod = ParalivesGameApiResolver.RegisterCharacterMethod;
                if (regMethod != null)
                {
                    regMethod.Invoke(charMgr, new object[] { clonedChar });
                    Plugin.Log.LogInfo($"[Paramulti] Registered game-native character for player {playerId}");
                }

                // Load visual
                var visual = ParalivesGameApiResolver.LoadCharacterVisualMethod.Invoke(charMgr, new object[] { guid });
                if (visual == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] LoadCharacterVisual returned null");
                    return null;
                }

                var transform = ExtractTransform(visual);
                if (transform == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] Could not extract transform from loaded CharacterVisual");
                    return null;
                }

                transform.position = spawnPos;
                transform.rotation = Quaternion.identity;

                StripInputComponents(transform);
                DisablePathfinding(clonedChar);

                Plugin.Log.LogInfo($"[Paramulti] Successfully spawned game-native character for player {playerId} (GUID={guid:X})");

                return new RemoteCharacterEntry
                {
                    PlayerId = playerId,
                    CharacterGuid = guid,
                    GameNativeCharacter = clonedChar,
                    ControlledTransform = transform,
                    IsGameNative = true,
                    LastKnownPosition = spawnPos,
                    LastKnownRotation = Quaternion.identity
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] Game-native spawn exception for player {playerId}: {ex.Message}");
                return null;
            }
        }

        static object TryFindCharacterAssetByModelGuid(ulong modelGuid)
        {
            try
            {
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                if (charMgr == null) return null;
                var charsProp = charMgr.GetType().GetProperty("Characters",
                    BindingFlags.Public | BindingFlags.Instance);
                var chars = charsProp?.GetValue(charMgr) as System.Collections.IList;
                if (chars == null) return null;
                foreach (var c in chars)
                {
                    var dataProp = c.GetType().GetProperty("Data");
                    var data = dataProp?.GetValue(c);
                    if (data == null) continue;
                    var modelField = data.GetType().GetField("CurrentCharacterModelGUID",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (modelField == null) modelField = data.GetType().GetField("CharacterModelGUID",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (modelField == null) continue;
                    var mg = (ulong)modelField.GetValue(data);
                    if (mg == modelGuid)
                    {
                        Plugin.Log.LogInfo($"[Paramulti] TryFindCharacterAssetByModelGuid: matched model={modelGuid:X}");
                        return c;
                    }
                }
                Plugin.Log.LogInfo($"[Paramulti] TryFindCharacterAssetByModelGuid: no character found with model={modelGuid:X}");
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] TryFindCharacterAssetByModelGuid error: {ex.Message}");
                return null;
            }
        }

        static RemoteCharacterEntry TryCreatePrefabClone(int playerId, string playerName, Vector3 spawnPos)
        {
            try
            {
                // Clone the LOCAL player's CharacterVisual as the proxy
                // This gives us a visible character model (even if it's the wrong appearance)
                if (_localCharacterTransform == null)
                {
                    Plugin.Log.LogWarning($"[Paramulti] Cannot clone local character for player {playerId}: local transform is null");
                    return null;
                }

                var localGo = _localCharacterTransform.gameObject;
                var go = UnityEngine.Object.Instantiate(localGo);
                go.name = $"[Remote:{playerId}] {playerName}";
                var transform = go.transform;
                transform.position = spawnPos;
                transform.rotation = Quaternion.identity;

                // Count components before stripping
                var animators = go.GetComponentsInChildren<Animator>(true).Length;
                var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                var meshes = go.GetComponentsInChildren<MeshRenderer>(true).Length;

                StripInputComponents(transform);
                ApplyGhostMaterial(go, playerId);
                AttachDebugMarker(go, playerId, playerName);

                Plugin.Log.LogInfo($"[Paramulti] Cloned local character for player {playerId}: {go.name} at {spawnPos} (Animator={animators}, SkinnedMesh={skinned}, MeshRenderer={meshes})");

                return new RemoteCharacterEntry
                {
                    PlayerId = playerId,
                    CharacterGuid = GenerateGuidForPlayer(playerId),
                    ControlledTransform = transform,
                    FallbackProxy = go,
                    IsGameNative = false,
                    LastKnownPosition = spawnPos,
                    LastKnownRotation = Quaternion.identity
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] Local character clone failed for player {playerId}: {ex.Message}");
                return null;
            }
        }

        static RemoteCharacterEntry CreateFallbackProxy(int playerId, string playerName, Vector3 spawnPos)
        {
            try
            {
                // Use Unity's built-in cube primitive — guaranteed to render correctly
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"[Remote:{playerId}] {playerName}";
                go.tag = "Untagged";

                var transform = go.transform;
                // Use spawn position directly — game Y is already at character height
                transform.position = new Vector3(spawnPos.x, spawnPos.y, spawnPos.z);
                transform.rotation = Quaternion.identity;
                // Scale 1,1,1 so child visuals (game-native, prefab clone) render at natural size
                transform.localScale = Vector3.one;

                // Remove the default collider (we don't need physics on proxy)
                var collider = go.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);

                // Get the existing renderer and change to bright glowing color
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    var color = GetPlayerColor(playerId);
                    // Use the cube's existing Standard material — just tint it + add emission
                    renderer.material.color = color;
                    renderer.material.SetColor("_EmissionColor", color * 0.5f);
                    renderer.material.EnableKeyword("_EMISSION");
                }

                StripInputComponents(transform);
                AttachDebugMarker(go, playerId, playerName);

                Plugin.Log.LogInfo($"[Paramulti] Created fallback proxy (CUBE) for player {playerId}: {go.name} at {transform.position}, scale={transform.localScale}");

                return new RemoteCharacterEntry
                {
                    PlayerId = playerId,
                    CharacterGuid = GenerateGuidForPlayer(playerId),
                    ControlledTransform = transform,
                    FallbackProxy = go,
                    IsGameNative = false,
                    LastKnownPosition = spawnPos,
                    LastKnownRotation = Quaternion.identity
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] Failed to create fallback proxy for player {playerId}: {ex.Message}");
                return null;
            }
        }

        static Mesh CreateSimpleMesh()
        {
            try
            {
                var mesh = new Mesh();
                mesh.name = "RemotePlayerProxy";

                // Large human-sized box: 1.5 wide, 2.5 tall, 1.5 deep
                float w = 0.75f, h = 1.25f, d = 0.75f;

                // 8 corners of the box
                var vertices = new Vector3[8];
                vertices[0] = new Vector3(-w, 0f, -d);  // bottom-back-left
                vertices[1] = new Vector3(w, 0f, -d);   // bottom-back-right
                vertices[2] = new Vector3(-w, h, -d);   // top-back-left
                vertices[3] = new Vector3(w, h, -d);    // top-back-right
                vertices[4] = new Vector3(-w, 0f, d);   // bottom-front-left
                vertices[5] = new Vector3(w, 0f, d);    // bottom-front-right
                vertices[6] = new Vector3(-w, h, d);    // top-front-left
                vertices[7] = new Vector3(w, h, d);     // top-front-right

                // 12 triangles (6 faces * 2), counter-clockwise winding from outside
                var triangles = new int[]
                {
                    // Front face (Z+)
                    4, 5, 7,
                    4, 7, 6,
                    // Back face (Z-)
                    1, 0, 2,
                    1, 2, 3,
                    // Right face (X+)
                    5, 1, 3,
                    5, 3, 7,
                    // Left face (X-)
                    0, 4, 6,
                    0, 6, 2,
                    // Top face (Y+)
                    6, 7, 3,
                    6, 3, 2,
                    // Bottom face (Y-)
                    0, 1, 5,
                    0, 5, 4
                };

                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.RecalculateNormals();
                return mesh;
            }
            catch
            {
                return null;
            }
        }

        static Color GetPlayerColor(int playerId)
        {
            var colors = new Color[]
            {
                new Color(1f, 0.4f, 0.4f),
                new Color(0.4f, 0.4f, 1f),
                new Color(0.4f, 1f, 0.4f),
                new Color(1f, 1f, 0.4f),
            };
            return colors[playerId % colors.Length];
        }

        static void AttachDebugMarker(GameObject parent, int playerId, string playerName)
        {
            try
            {
                var color = GetPlayerColor(playerId);

                // 1) Big glowing sphere above head
                var marker = new GameObject($"[DebugMarker:{playerId}]");
                marker.transform.SetParent(parent.transform, false);
                marker.transform.localPosition = new Vector3(0f, 2.5f, 0f);
                marker.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);

                var filter = marker.AddComponent<MeshFilter>();
                filter.mesh = CreateDebugSphereMesh();

                var renderer = marker.AddComponent<MeshRenderer>();
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Diffuse");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                var mat = new Material(shader);
                mat.color = color;
                renderer.material = mat;

                // 2) Point light to make it glow
                var light = marker.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = color;
                light.intensity = 3f;
                light.range = 8f;

                // 3) Tall vertical beam so proxy is visible from far away
                var beam = new GameObject($"[Beam:{playerId}]");
                beam.transform.SetParent(parent.transform, false);
                beam.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                beam.transform.localScale = new Vector3(0.2f, 3.0f, 0.2f);
                var beamFilter = beam.AddComponent<MeshFilter>();
                beamFilter.mesh = CreateBeamMesh();
                var beamRenderer = beam.AddComponent<MeshRenderer>();
                var beamShader = Shader.Find("Standard");
                if (beamShader == null) beamShader = Shader.Find("Diffuse");
                if (beamShader == null) beamShader = Shader.Find("Unlit/Color");
                if (beamShader == null) beamShader = Shader.Find("Sprites/Default");
                var beamMat = new Material(beamShader);
                beamMat.color = new Color(color.r, color.g, color.b, 0.6f);
                beamRenderer.material = beamMat;

                // Floating nameplate
                AttachNameplate(parent, playerId, playerName);

                Plugin.Log.LogInfo($"[Paramulti] Attached debug marker + beam + nameplate to remote player {playerId} proxy");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] Debug marker failed for player {playerId}: {ex.Message}");
            }
        }

        static void AttachNameplate(GameObject parent, int playerId, string playerName)
        {
            try
            {
                var npGo = new GameObject($"[Nameplate:{playerId}]");
                npGo.transform.SetParent(parent.transform, false);
                npGo.transform.localPosition = new Vector3(0f, 2.3f, 0f);

                // Billboard script to always face camera
                var billboard = npGo.AddComponent<NameplateBillboard>();
                billboard.PlayerName = playerName;
                billboard.Color = GetPlayerColor(playerId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] Nameplate failed for player {playerId}: {ex.Message}");
            }
        }

        static void ApplyGhostMaterial(GameObject root, int playerId)
        {
            try
            {
                var color = GetPlayerColor(playerId);
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Diffuse");
                if (shader == null) shader = Shader.Find("Mobile/Diffuse");
                if (shader == null) return;

                var ghostMat = new Material(shader);
                ghostMat.color = new Color(color.r, color.g, color.b, 0.35f);
                // Enable transparency if Standard shader
                if (shader.name == "Standard")
                {
                    ghostMat.SetFloat("_Mode", 3f); // Transparent mode
                    ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    ghostMat.SetInt("_ZWrite", 0);
                    ghostMat.DisableKeyword("_ALPHATEST_ON");
                    ghostMat.EnableKeyword("_ALPHABLEND_ON");
                    ghostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    ghostMat.renderQueue = 3000;
                }

                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var rend in renderers)
                {
                    if (rend == null) continue;
                    // Don't ghost the debug marker itself
                    if (rend.gameObject.name.StartsWith("[DebugMarker")) continue;
                    if (rend.gameObject.name.StartsWith("[Nameplate")) continue;
                    rend.material = ghostMat;
                }

                var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var rend in skinnedRenderers)
                {
                    if (rend == null) continue;
                    rend.material = ghostMat;
                }

                Plugin.Log.LogInfo($"[Paramulti] Applied ghost material to remote player {playerId} proxy ({renderers.Length} mesh, {skinnedRenderers.Length} skinned)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] Ghost material failed for player {playerId}: {ex.Message}");
            }
        }

        static void MakeProxyHighlyVisible(GameObject root, int playerId)
        {
            try
            {
                var color = GetPlayerColor(playerId);
                // Use Unlit/Color first so proxy is bright regardless of lighting
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Diffuse");
                if (shader == null) shader = Shader.Find("Mobile/Diffuse");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return;

                var brightMat = new Material(shader);
                brightMat.color = color;

                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var rend in renderers)
                {
                    if (rend == null) continue;
                    if (rend.gameObject.name.StartsWith("[DebugMarker")) continue;
                    if (rend.gameObject.name.StartsWith("[Nameplate")) continue;
                    if (rend.gameObject.name.StartsWith("[Beam")) continue;
                    rend.material = brightMat;
                }

                var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var rend in skinnedRenderers)
                {
                    if (rend == null) continue;
                    rend.material = brightMat;
                }

                // Add a bright point light ON the proxy itself so the floor/walls glow
                var proxyLight = root.GetComponent<Light>();
                if (proxyLight == null)
                {
                    proxyLight = root.AddComponent<Light>();
                    proxyLight.type = LightType.Point;
                }
                proxyLight.color = color;
                proxyLight.intensity = 5f;
                proxyLight.range = 6f;

                Plugin.Log.LogInfo($"[Paramulti] Made proxy highly visible for player {playerId} ({renderers.Length} mesh, {skinnedRenderers.Length} skinned, shader={shader.name})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] MakeProxyHighlyVisible failed for player {playerId}: {ex.Message}");
            }
        }

        static Mesh CreateBeamMesh()
        {
            var mesh = new Mesh();
            mesh.name = "VerticalBeam";
            float w = 0.5f, h = 1.0f;
            var verts = new Vector3[4];
            verts[0] = new Vector3(-w, -h, 0f);
            verts[1] = new Vector3(w, -h, 0f);
            verts[2] = new Vector3(-w, h, 0f);
            verts[3] = new Vector3(w, h, 0f);
            var tris = new int[] { 0, 2, 1, 2, 3, 1 };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        static Mesh CreateDebugSphereMesh()
        {
            var mesh = new Mesh();
            mesh.name = "DebugSphere";
            var verts = new Vector3[12];
            float r = 0.5f;
            float phi = (1f + Mathf.Sqrt(5f)) / 2f;
            float a = r / Mathf.Sqrt(1 + phi * phi);
            float b = a * phi;

            verts[0] = new Vector3(-a, b, 0);
            verts[1] = new Vector3(a, b, 0);
            verts[2] = new Vector3(-a, -b, 0);
            verts[3] = new Vector3(a, -b, 0);
            verts[4] = new Vector3(0, -a, b);
            verts[5] = new Vector3(0, a, b);
            verts[6] = new Vector3(0, -a, -b);
            verts[7] = new Vector3(0, a, -b);
            verts[8] = new Vector3(b, 0, -a);
            verts[9] = new Vector3(b, 0, a);
            verts[10] = new Vector3(-b, 0, -a);
            verts[11] = new Vector3(-b, 0, a);

            var tris = new int[]
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,11,2, 6,10,2, 8,7,6, 9,5,4, 9,8,1
            };

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        static ulong GenerateGuidForPlayer(int playerId)
        {
            return ((ulong)0xDEAD << 56) | ((ulong)(playerId + 1) << 48) | (ulong)0xBEEF00000000L;
        }

        public static void RemoveRemoteCharacter(int playerId)
        {
            RemoteCharacterEntry entry = null;
            lock (_lock)
            {
                if (!_remoteCharacters.TryGetValue(playerId, out entry)) return;
                _remoteCharacters.Remove(playerId);
            }

            Plugin.Log.LogInfo($"[Paramulti] Removing character for player {playerId}");

            if (entry.IsGameNative && entry.GameNativeCharacter != null &&
                ParalivesGameApiResolver.CharacterManagerInstance != null)
            {
                try
                {
                    // We synthesized this AssetCharacter via _assets injection — cleanup is manual.
                    // DO NOT call DeleteCharacter: it walks the household, tries Unload() (filesystem),
                    // and DeleteAsset() on a clone that has no on-disk file.
                    RemoveFromLoadedCharacterVisuals(entry.CharacterGuid);
                    RemoveFromCharacterManagerList(entry.GameNativeCharacter);
                    RemoveFromAssetManagerAssets(entry.CharacterGuid);
                    Plugin.Log.LogInfo($"[Paramulti] Cleaned up synthesized AssetCharacter for player {playerId} (GUID={entry.CharacterGuid:X})");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Paramulti] Failed to clean up game-native character for player {playerId}: {ex.Message}");
                }
            }

            if (entry.FallbackProxy != null)
            {
                DestroyGameObject(entry.FallbackProxy);
            }
            else if (entry.ControlledTransform != null)
            {
                DestroyGameObject(entry.ControlledTransform.gameObject);
            }

            // Remove from household to keep save files clean
            HouseholdSyncManager.RemoveRemoteCharacterFromHousehold(entry.CharacterGuid);
            HouseholdSyncManager.TriggerUIRefresh();

            OnRemoteCharacterRemoved?.Invoke(playerId);
        }

        public static bool TryGetTransform(int playerId, out Transform transform)
        {
            transform = null;
            lock (_lock)
            {
                if (_remoteCharacters.TryGetValue(playerId, out var entry))
                {
                    transform = entry.ControlledTransform;
                    return transform != null;
                }
            }
            return false;
        }

        public static RemoteCharacterEntry GetRemoteCharacterEntry(int playerId)
        {
            lock (_lock)
            {
                _remoteCharacters.TryGetValue(playerId, out var entry);
                return entry;
            }
        }

        public static void ApplyRemoteState(int playerId, Vector3 position, Quaternion rotation)
        {
            lock (_lock)
            {
                if (!_remoteCharacters.TryGetValue(playerId, out var entry)) return;
                if (entry.ControlledTransform == null) return;

                // Reject void/character-creator positions. The sender might be in the character creator
                // and broadcast a transform from the void; we should not teleport the proxy there.
                // Keep the last known valid position instead.
                if (!IsValidWorldPosition(position))
                {
                    Plugin.Log.LogInfo($"[Paramulti][Sync] Rejecting void position for Player {playerId}: {position} (keeping last known)");
                    return;
                }

                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;

                Plugin.Log.LogInfo($"[Paramulti][Sync] Applying transform for Player {playerId}. pos={position}, rot={rotation}");

                try
                {
                    if (entry.IsGameNative && entry.GameNativeCharacter != null)
                    {
                        // The visual is parented to CharacterManager. The game sets
                        //   visual.localPosition = data.Position + (0, num4, 0)
                        // every frame, where num4 = Visual.Data.CharacterYOffset * RigRootScale.
                        // For the visual's WORLD position to equal the desired position:
                        //   CharacterManager.position + data.Position + (0, num4, 0) = position
                        //   data.Position = position - CharacterManager.position - (0, num4, 0)
                        var parent = entry.ControlledTransform.parent;
                        Vector3 parentOffset = parent != null ? parent.position : Vector3.zero;

                        // Read num4 = CharacterYOffset * RigRootScale
                        float num4 = 0f;
                        var dataProp = entry.GameNativeCharacter.GetType().GetProperty("Data");
                        var data = dataProp?.GetValue(entry.GameNativeCharacter);
                        if (data != null)
                        {
                            var visualProp = entry.GameNativeCharacter.GetType().GetProperty("Visual");
                            var visual = visualProp?.GetValue(entry.GameNativeCharacter);
                            if (visual != null)
                            {
                                var visualDataProp = visual.GetType().GetProperty("Data");
                                var visualData = visualDataProp?.GetValue(visual);
                                if (visualData != null)
                                {
                                    var yOffsetField = visualData.GetType().GetProperty("CharacterYOffset");
                                    if (yOffsetField != null) num4 = (float)yOffsetField.GetValue(visualData);
                                }
                            }
                            // Multiply by RigRootScale (read live from the visual transform)
                            var rigRootProp = entry.ControlledTransform.GetType().GetProperty("RigRootScale");
                            if (rigRootProp != null) num4 *= (float)rigRootProp.GetValue(entry.ControlledTransform);
                        }

                        // Compute localPos such that the visual's world position equals `position`
                        Vector3 localPos = position - parentOffset - new Vector3(0f, num4, 0f);

                        if (data != null)
                        {
                            var dataType = data.GetType();
                            var posField = dataType.GetField("Position", BindingFlags.Public | BindingFlags.Instance);
                            posField?.SetValue(data, localPos);
                            var rotField = dataType.GetField("Rotation", BindingFlags.Public | BindingFlags.Instance);
                            rotField?.SetValue(data, rotation);
                            var lastPosField = dataType.GetField("LastPositionUsedForZoneObject");
                            lastPosField?.SetValue(data, localPos);

                            // Also set the visual's world position directly so the change is
                            // immediately visible (the game's update only runs every frame).
                            entry.ControlledTransform.position = position;
                            entry.ControlledTransform.rotation = rotation;

                            // DIAG: log the math so we can verify
                            Plugin.Log.LogInfo($"[Paramulti][Sync] Player {playerId} ApplyRemoteState math: parentOffset={parentOffset}, num4={num4}, localPos={localPos}, target={position}, actualWorld={entry.ControlledTransform.position}");
                        }
                        return;
                    }

                    entry.ControlledTransform.position = position;
                    entry.ControlledTransform.rotation = rotation;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Paramulti] Failed to apply state for player {playerId}: {ex.Message}");
                    entry.ControlledTransform.position = position;
                    entry.ControlledTransform.rotation = rotation;
                }
            }
        }

        public static void OnSessionEnd()
        {
            lock (_lock)
            {
                var playerIds = new List<int>(_remoteCharacters.Keys);
                foreach (var id in playerIds)
                    RemoveRemoteCharacter(id);
            }

            _localCharacterTransform = null;
            _localCharacterFound = false;
            Plugin.Log.LogInfo("[Paramulti] All remote characters cleaned up");
        }

        static void StripInputComponents(Transform root)
        {
            if (root == null) return;

            // IMPORTANT: Do NOT strip Animator — SkinnedMeshRenderer needs it enabled to render!
            // Only strip input/movement/physics components.
            var inputComponentNames = new string[]
            {
                "PlayerInput", "InputManager", "CharacterController",
                "Rigidbody", "NavMeshAgent",
                "PlayerController", "MovementController", "InputHandler",
                "CameraController", "ThirdPersonController", "FirstPersonController",
                "HybridPlayer"
            };

            var toDisable = new List<Component>();
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                var typeName = comp.GetType().Name.ToUpperInvariant();
                bool shouldStrip = false;
                foreach (var name in inputComponentNames)
                {
                    if (typeName.IndexOf(name.ToUpperInvariant(), StringComparison.Ordinal) >= 0)
                    {
                        shouldStrip = true;
                        break;
                    }
                }
                if (shouldStrip)
                {
                    toDisable.Add(comp);
                }
            }

            int stripped = 0;
            foreach (var comp in toDisable)
            {
                try
                {
                    var behaviour = comp as Behaviour;
                    if (behaviour != null)
                    {
                        behaviour.enabled = false;
                        stripped++;
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(comp);
                        stripped++;
                    }
                }
                catch
                {
                    try { UnityEngine.Object.DestroyImmediate(comp); stripped++; } catch { }
                }
            }

            if (stripped > 0)
            {
                Plugin.Log.LogInfo($"[Paramulti] Stripped {stripped} input/control components from remote character (Animator KEPT for rendering)");
            }
        }

        static void ForceStandardMaterials(GameObject root)
        {
            if (root == null) return;
            try
            {
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Diffuse");
                if (shader == null) return;

                int replaced = 0;
                var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var rend in skinnedRenderers)
                {
                    if (rend == null || !rend.enabled) continue;
                    var mats = rend.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;
                    var newMats = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        newMats[i] = new Material(shader);
                        newMats[i].color = Color.white;
                    }
                    rend.materials = newMats;
                    replaced += mats.Length;
                }

                var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var rend in meshRenderers)
                {
                    if (rend == null || !rend.enabled) continue;
                    if (rend.gameObject.name.StartsWith("[DebugMarker")) continue;
                    if (rend.gameObject.name.StartsWith("[Nameplate")) continue;
                    if (rend.gameObject.name.StartsWith("[Beam")) continue;
                    var mats = rend.sharedMaterials;
                    if (mats == null || mats.Length == 0) continue;
                    var newMats = new Material[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        newMats[i] = new Material(shader);
                        newMats[i].color = Color.white;
                    }
                    rend.materials = newMats;
                    replaced += mats.Length;
                }

                if (replaced > 0)
                    Plugin.Log.LogInfo($"[Paramulti] ForceStandardMaterials: replaced {replaced} materials on cloned character");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] ForceStandardMaterials error: {ex.Message}");
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

            // AssetCharacterVisual — data asset with no scene presence.
            // Get the runtime visual via its Data object's CurrentCharacterModelGUID,
            // then look up via CharacterManager.GetCharacterByGUID or GetLoadedCharacterVisual.
            if (obj.GetType().Name == "AssetCharacterVisual")
            {
                try
                {
                    // 1. Read CurrentCharacterModelGUID from AssetCharacterVisual.Data
                    var dataProp = obj.GetType().GetProperty("Data");
                    if (dataProp != null)
                    {
                        var visualData = dataProp.GetValue(obj);
                        if (visualData != null)
                        {
                            var dataType = visualData.GetType();
                            var modelGuidField = dataType.GetField("CurrentCharacterModelGUID",
                                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            if (modelGuidField != null)
                            {
                                ulong modelGuid = (ulong)modelGuidField.GetValue(visualData);

                                if (modelGuid != 0 && ParalivesGameApiResolver.CharacterManagerInstance != null)
                                {
                                    var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;

                                    // Try GetCharacterByGUID — this should give us the AssetCharacter
                                    var getCharMethod = ParalivesGameApiResolver.GetCharacterByGUIDMethod;
                                    if (getCharMethod != null)
                                    {
                                        var assetChar = getCharMethod.Invoke(charMgr, new object[] { modelGuid });
                                        if (assetChar != null)
                                        {
                                            // Get the visual from this AssetCharacter
                                            var visProp = assetChar.GetType().GetProperty("Visual");
                                            var runtimeVisual = visProp?.GetValue(assetChar);
                                            if (runtimeVisual != null)
                                            {
                                                var t2 = ExtractTransform(runtimeVisual);
                                                if (t2 != null) return t2;
                                            }
                                        }
                                    }

                                    // Try GetLoadedCharacterVisual(modelGuid)
                                    var loadedMethod = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                                    if (loadedMethod != null)
                                    {
                                        var runtimeVisual = loadedMethod.Invoke(charMgr, new object[] { modelGuid });
                                        if (runtimeVisual != null)
                                        {
                                            var t2 = ExtractTransform(runtimeVisual);
                                            if (t2 != null) return t2;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // 2. Fallback: iterate CharacterManager.Characters by reference match
                    if (ParalivesGameApiResolver.CharacterManagerInstance != null)
                    {
                        var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                        var cmType = ParalivesGameApiResolver.CharacterManagerType;
                        var charsProp = cmType.GetProperty("Characters");
                        if (charsProp != null)
                        {
                            var chars = charsProp.GetValue(charMgr) as System.Collections.IList;
                            if (chars != null)
                            {
                                foreach (var c in chars)
                                {
                                    var visualProp = c.GetType().GetProperty("Visual");
                                    var visual = visualProp?.GetValue(c);
                                    bool isMatch = ReferenceEquals(visual, obj) || visual == obj;
                                    if (isMatch)
                                    {
                                        // Try all possible GUID sources on the AssetCharacter
                                        ulong charGUID = 0;
                                        var guidProp = c.GetType().GetProperty("GUID",
                                            BindingFlags.Public | BindingFlags.Instance);
                                        if (guidProp != null) charGUID = (ulong)guidProp.GetValue(c);
                                        if (charGUID == 0)
                                        {
                                            var f = c.GetType().GetField("m_CharacterGUID",
                                                BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (f != null) charGUID = (ulong)f.GetValue(c);
                                        }
                                        if (charGUID == 0)
                                        {
                                            var f2 = c.GetType().BaseType?.GetField("m_GUID",
                                                BindingFlags.NonPublic | BindingFlags.Instance);
                                            if (f2 != null) charGUID = (ulong)f2.GetValue(c);
                                        }
                                        if (charGUID != 0)
                                        {
                                            var loadedMethod = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                                            if (loadedMethod != null)
                                            {
                                                var rv = loadedMethod.Invoke(charMgr, new object[] { charGUID });
                                                if (rv != null)
                                                {
                                                    var t2 = ExtractTransform(rv);
                                                    if (t2 != null) return t2;
                                                }
                                            }
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // AssetCharacterVisual is a data asset — no runtime transform available
                }

                return null;
            }

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

            // Unknown type — no transform found
            return null;
        }

        static void DestroyGameObject(GameObject go)
        {
            if (go != null)
            {
                try { UnityEngine.Object.DestroyImmediate(go); } catch { }
                try { UnityEngine.Object.Destroy(go); } catch { }
            }
        }

        // --- Character Ownership + Data Sync ---

        public static ulong GetLocalCharacterGuid()
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null) return 0;
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var cmType = ParalivesGameApiResolver.CharacterManagerType;
                var charsProp = cmType.GetProperty("Characters");
                if (charsProp == null) return 0;
                var chars = charsProp.GetValue(charMgr) as System.Collections.IList;
                if (chars == null) return 0;

                foreach (var c in chars)
                {
                    var visualProp = c.GetType().GetProperty("Visual");
                    var visual = visualProp?.GetValue(c);
                    var t = ExtractTransform(visual);
                    if (t == _localCharacterTransform)
                    {
                        return GetAssetGuid(c);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] GetLocalCharacterGuid error: {ex.Message}");
            }
            return 0;
        }

        public static void RegisterLocalCharacterOwnership()
        {
            var guid = GetLocalCharacterGuid();
            if (guid != 0)
            {
                CharacterOwnershipManager.RegisterOwnership(MultiplayerSession.LocalPlayerId, guid);
                Plugin.Log.LogInfo($"[Paramulti] Registered local ownership: Player {MultiplayerSession.LocalPlayerId} -> GUID={guid:X}");
            }
        }

        // Build a MsgCharacterDataSync from any AssetCharacter (used by both the selected-sim
        // shortcut and the transform-match path).
        static ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync BuildCharacterDataSyncFromAssetCharacter(object c)
        {
            try
            {
                if (c == null) return null;
                var guid = GetAssetGuid(c);
                var dataProp = c.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(c);
                if (data == null) return null;

                var dataType = data.GetType();
                var firstNameField = dataType.GetField("FirstName");
                var fullNameField = dataType.GetField("FullName");
                var ageField = dataType.GetField("Age");
                var speciesField = dataType.GetField("CurrentSpeciesGUID");
                var modelField = dataType.GetField("CurrentCharacterModelGUID");
                var postureField = dataType.GetField("CurrentPosture");
                var deadField = dataType.GetField("IsDeadOrTakenAway");

                // Position/rotation should come from THIS sim's runtime visual — not the cached
                // _localCharacterTransform which may belong to a different household member.
                NetVector3 pos = new NetVector3(0f, 0f, 0f);
                NetQuaternion rot = new NetQuaternion(0f, 0f, 0f, 1f);
                bool havePos = false;
                var thisTransform = TryGetRuntimeTransformForCharacter(c, guid);
                if (thisTransform != null && IsValidWorldPosition(thisTransform.position))
                {
                    pos = thisTransform.position.FromUnity();
                    rot = thisTransform.rotation.FromUnity();
                    havePos = true;
                }
                else if (_localCharacterTransform != null && IsValidWorldPosition(_localCharacterTransform.position))
                {
                    pos = _localCharacterTransform.position.FromUnity();
                    rot = _localCharacterTransform.rotation.FromUnity();
                    havePos = true;
                }
                if (!havePos)
                {
                    Plugin.Log.LogInfo($"[Paramulti] Skipping sync for GUID={guid:X}: no valid world position (selected sim is in void/creator space)");
                    return null;
                }

                return new ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    CharacterGuid = guid,
                    FirstName = firstNameField != null ? (string)firstNameField.GetValue(data) : "Player",
                    FullName = fullNameField != null ? (string)fullNameField.GetValue(data) : $"Player_{MultiplayerSession.LocalPlayerId}",
                    Age = ageField != null ? (float)ageField.GetValue(data) : 0f,
                    SpeciesGuid = speciesField != null ? (ulong)speciesField.GetValue(data) : 0UL,
                    CharacterModelGuid = modelField != null ? (ulong)modelField.GetValue(data) : 0UL,
                    CurrentPostureGuid = postureField != null ? (ulong)postureField.GetValue(data) : 0UL,
                    IsDeadOrTakenAway = deadField != null ? (bool)deadField.GetValue(data) : false,
                    LastKnownPosition = pos,
                    LastKnownRotation = rot,
                    CharacterVisualJson = TrySerializeVisualJsonFromCharacter(c)
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] BuildCharacterDataSyncFromAssetCharacter failed: {ex.Message}");
                return null;
            }
        }

        // CharacterManager.GetLoadedCharacterVisual(guid) → CharacterVisual → transform.
        // Returns the transform of the live in-world model for this AssetCharacter, or null.
        static Transform TryGetRuntimeTransformForCharacter(object assetCharacter, ulong guid)
        {
            try
            {
                if (guid == 0) guid = GetAssetGuid(assetCharacter);
                if (guid == 0) return null;
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var method = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                if (charMgr == null || method == null) return null;
                var visual = method.Invoke(charMgr, new object[] { guid });
                return ExtractTransform(visual);
            }
            catch { return null; }
        }

        public static ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync BuildLocalCharacterDataSync()
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] BuildLocalCharacterDataSync: CharacterManager instance is null");
                    return null;
                }

                // Preferred path: ask the game for the actively-selected sim. This is the one the
                // player is controlling, so its data is what other clients should render.
                var selected = TryGetSelectedAssetCharacter();
                if (selected != null)
                {
                    var msg = BuildCharacterDataSyncFromAssetCharacter(selected);
                    if (msg != null)
                    {
                        Plugin.Log.LogInfo($"[Paramulti] BuildLocalCharacterDataSync (selected): GUID={msg.CharacterGuid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}, pos=({msg.LastKnownPosition.x:0.00}, {msg.LastKnownPosition.y:0.00}, {msg.LastKnownPosition.z:0.00}), visualJsonBytes={msg.CharacterVisualJson?.Length ?? 0}");
                        return msg;
                    }
                }

                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var cmType = ParalivesGameApiResolver.CharacterManagerType;
                var charsProp = cmType.GetProperty("Characters");
                if (charsProp == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] BuildLocalCharacterDataSync: Characters property not found");
                    return null;
                }

                var chars = charsProp.GetValue(charMgr) as System.Collections.IList;
                if (chars == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] BuildLocalCharacterDataSync: Characters list is null");
                    return null;
                }

                Plugin.Log.LogInfo($"[Paramulti] BuildLocalCharacterDataSync: scanning {chars.Count} characters for transform match...");

                foreach (var c in chars)
                {
                    var visualProp = c.GetType().GetProperty("Visual");
                    var visual = visualProp?.GetValue(c);
                    var t = ExtractTransform(visual);
                    bool match = t == _localCharacterTransform;
                    Plugin.Log.LogDebug($"[Paramulti]   char visual transform: {t?.name ?? "null"}, match={match}");
                    if (!match) continue;

                    var guid = GetAssetGuid(c);

                    var dataProp = c.GetType().GetProperty("Data");
                    var data = dataProp?.GetValue(c);
                    if (data == null)
                    {
                        Plugin.Log.LogWarning("[Paramulti] BuildLocalCharacterDataSync: matched character but Data is null, using fallback");
                        return BuildFallbackCharacterDataSync(guid);
                    }

                    var dataType = data.GetType();
                    var firstNameField = dataType.GetField("FirstName");
                    var fullNameField = dataType.GetField("FullName");
                    var ageField = dataType.GetField("Age");
                    var speciesField = dataType.GetField("CurrentSpeciesGUID");
                    var modelField = dataType.GetField("CurrentCharacterModelGUID");
                    var postureField = dataType.GetField("CurrentPosture");
                    var deadField = dataType.GetField("IsDeadOrTakenAway");

                    var msg = new ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync
                    {
                        PlayerId = MultiplayerSession.LocalPlayerId,
                        CharacterGuid = guid,
                        FirstName = firstNameField != null ? (string)firstNameField.GetValue(data) : "Player",
                        FullName = fullNameField != null ? (string)fullNameField.GetValue(data) : $"Player_{MultiplayerSession.LocalPlayerId}",
                        Age = ageField != null ? (float)ageField.GetValue(data) : 0f,
                        SpeciesGuid = speciesField != null ? (ulong)speciesField.GetValue(data) : 0UL,
                        CharacterModelGuid = modelField != null ? (ulong)modelField.GetValue(data) : 0UL,
                        CurrentPostureGuid = postureField != null ? (ulong)postureField.GetValue(data) : 0UL,
                        IsDeadOrTakenAway = deadField != null ? (bool)deadField.GetValue(data) : false,
                        LastKnownPosition = _localCharacterTransform != null ? _localCharacterTransform.position.FromUnity() : new NetVector3(0f, 0f, 0f),
                        LastKnownRotation = _localCharacterTransform != null ? _localCharacterTransform.rotation.FromUnity() : new NetQuaternion(0f, 0f, 0f, 1f),
                        CharacterVisualJson = TrySerializeVisualJsonFromCharacter(c)
                    };

                    Plugin.Log.LogInfo($"[Paramulti] Built local character data sync: GUID={guid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}, visualJsonBytes={msg.CharacterVisualJson?.Length ?? 0}");
                    return msg;
                }

                // Fallback: no character in the list matched our local transform, but we have a transform
                if (_localCharacterTransform != null)
                {
                    // In live world the camera-follow transform often won't match CharacterManager entries
                    Plugin.Log.LogInfo("[Paramulti] BuildLocalCharacterDataSync: using fallback (camera-follow transform not in CharacterManager list)");
                    return BuildFallbackCharacterDataSync(0);
                }

                // Emergency fallback: we don't have a local character transform yet, but session is active.
                // Send minimal data so the other side at least creates a proxy.
                Plugin.Log.LogWarning("[Paramulti] BuildLocalCharacterDataSync: no local character transform yet, sending emergency minimal data");
                return BuildMinimalCharacterDataSync();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] BuildLocalCharacterDataSync error: {ex.Message}");
            }
            return null;
        }

        static ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync BuildMinimalCharacterDataSync()
        {
            if (_localCharacterTransform == null || !IsValidWorldPosition(_localCharacterTransform.position))
            {
                Plugin.Log.LogInfo("[Paramulti] Skipping MINIMAL sync: no valid world position");
                return null;
            }
            var guid = GenerateGuidForPlayer(MultiplayerSession.LocalPlayerId);
            var pos = _localCharacterTransform.position.FromUnity();
            var rot = _localCharacterTransform.rotation.FromUnity();

            var modelGuid = GetLocalCharacterModelGuid();
            var msg = new ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                CharacterGuid = guid,
                FirstName = $"Player_{MultiplayerSession.LocalPlayerId}",
                FullName = $"Player_{MultiplayerSession.LocalPlayerId}",
                Age = 0f,
                SpeciesGuid = 0UL,
                CharacterModelGuid = modelGuid,
                CurrentPostureGuid = 0UL,
                IsDeadOrTakenAway = false,
                LastKnownPosition = pos,
                LastKnownRotation = rot,
                CharacterVisualJson = TrySerializeLocalCharacterVisualJson()
            };

            Plugin.Log.LogInfo($"[Paramulti] Built MINIMAL character data sync: GUID={guid:X}, Name={msg.FullName}, Model={modelGuid:X}, pos={pos}, visualJsonBytes={msg.CharacterVisualJson?.Length ?? 0}");
            return msg;
        }

        static ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync BuildFallbackCharacterDataSync(ulong guid)
        {
            if (_localCharacterTransform == null || !IsValidWorldPosition(_localCharacterTransform.position))
            {
                Plugin.Log.LogInfo("[Paramulti] Skipping FALLBACK sync: no valid world position");
                return null;
            }
            if (guid == 0)
                guid = GetLocalCharacterGuid();
            if (guid == 0)
                guid = GenerateGuidForPlayer(MultiplayerSession.LocalPlayerId);

            var goName = _localCharacterTransform.gameObject?.name ?? "Unknown";
            var pos = _localCharacterTransform.position.FromUnity();
            var rot = _localCharacterTransform.rotation.FromUnity();

            var modelGuid = GetLocalCharacterModelGuid();
            var msg = new ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                CharacterGuid = guid,
                FirstName = $"Player_{MultiplayerSession.LocalPlayerId}",
                FullName = $"Player_{MultiplayerSession.LocalPlayerId}",
                Age = 0f,
                SpeciesGuid = 0UL,
                CharacterModelGuid = modelGuid,
                CurrentPostureGuid = 0UL,
                IsDeadOrTakenAway = false,
                LastKnownPosition = pos,
                LastKnownRotation = rot,
                CharacterVisualJson = TrySerializeLocalCharacterVisualJson()
            };

            Plugin.Log.LogInfo($"[Paramulti] Built FALLBACK character data sync: GUID={guid:X}, Name={msg.FullName}, Model={modelGuid:X}, transform={goName}, pos={pos}, visualJsonBytes={msg.CharacterVisualJson?.Length ?? 0}");
            return msg;
        }

        public static void ApplyRemoteCharacterDataSync(ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync msg)
        {
            if (msg == null) return;

            // Drop messages echoed back to us via the host's rebroadcast.
            if (msg.PlayerId == MultiplayerSession.LocalPlayerId)
            {
                Plugin.Log.LogDebug($"[Paramulti] Ignoring echoed self sync (PlayerId={msg.PlayerId})");
                return;
            }

            // Use a SYNTHETIC, per-player GUID for the proxy in _assets. The sender's
            // msg.CharacterGuid may collide with our own local AssetCharacters (e.g. when both
            // peers play a shared save). Synthesizing avoids the collision entirely while still
            // letting us inject + LoadCharacterVisual normally. msg.CharacterGuid is kept only
            // for logging context.
            ulong proxyGuid = GenerateGuidForPlayer(msg.PlayerId);

            Plugin.Log.LogInfo($"[Paramulti] Applying remote character data sync from player {msg.PlayerId}: sourceGUID={msg.CharacterGuid:X}, proxyGUID={proxyGuid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}, pos={msg.LastKnownPosition}");

            CharacterOwnershipManager.RegisterOwnership(msg.PlayerId, proxyGuid);

            Vector3 spawnPos = msg.LastKnownPosition.ToUnity();

            // Existing proxy? Update position; no GUID-change tracking needed because proxyGuid
            // is stable per playerId.
            lock (_lock)
            {
                if (_remoteCharacters.TryGetValue(msg.PlayerId, out var existingEntry))
                {
                    var newPos = msg.LastKnownPosition.ToUnity();
                    var newRot = msg.LastKnownRotation.ToUnity();
                    if (existingEntry.ControlledTransform != null)
                    {
                        existingEntry.ControlledTransform.position = newPos;
                        existingEntry.ControlledTransform.rotation = newRot;
                        existingEntry.LastKnownPosition = newPos;
                        existingEntry.LastKnownRotation = newRot;
                    }
                    Plugin.Log.LogInfo($"[Paramulti] Updated existing proxy for player {msg.PlayerId} to pos={newPos}");
                    return;
                }
            }

            // Step 1: Always create the visible fallback cube FIRST — guaranteed to render and serves as parent
            var entry = CreateFallbackProxy(msg.PlayerId, msg.FullName, spawnPos);
            if (entry == null)
            {
                Plugin.Log.LogError($"[Paramulti] CRITICAL: Could not create fallback proxy for player {msg.PlayerId}");
                return;
            }
            entry.CharacterGuid = proxyGuid;
            entry.ControlledTransform.position = spawnPos;
            entry.ControlledTransform.rotation = msg.LastKnownRotation.ToUnity();

            // Step 2: Spawn a game-native CharacterVisual using the synthesized proxyGuid as the
            // injection key (NOT msg.CharacterGuid, which may collide with our real local assets).
            Plugin.Log.LogInfo($"[Paramulti] Step2: proxyGuid={proxyGuid:X}, sourceGuid={msg.CharacterGuid:X}, modelGuid={msg.CharacterModelGuid:X}");
            if (ParalivesGameApiResolver.CharacterManagerInstance != null &&
                ParalivesGameApiResolver.AssetManagerInstance != null &&
                ParalivesGameApiResolver.LoadCharacterVisualMethod != null)
            {
                bool injected = false;
                object clonedChar = null;
                try
                {
                    var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;

                    // Find a donor to clone — prefer same model GUID, fall back to any local character.
                    object donor = null;
                    if (msg.CharacterModelGuid != 0)
                        donor = TryFindCharacterAssetByModelGuid(msg.CharacterModelGuid);
                    if (donor == null)
                        donor = TryFindAnyLocalAssetCharacter();
                    Plugin.Log.LogInfo($"[Paramulti] Step2: donor={(donor != null ? donor.GetType().Name : "null")}, donorGUID={GetAssetGuid(donor):X}");

                    if (donor != null)
                    {
                        var cloneMethod = donor.GetType().GetMethod("MemberwiseClone",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        clonedChar = cloneMethod?.Invoke(donor, null);

                        if (clonedChar != null)
                        {
                            SetAssetGuid(clonedChar, proxyGuid);
                            ForcePremadeTypeNo(clonedChar);

                            bool dataIsolated = TryIsolateClonedCharacterData(clonedChar);
                            Plugin.Log.LogInfo($"[Paramulti] Step2: data isolated={dataIsolated}");

                            bool visualApplied = TryApplyRemoteVisualToClone(clonedChar, donor, msg.CharacterVisualJson);
                            Plugin.Log.LogInfo($"[Paramulti] Step2: remote visual applied={visualApplied}, jsonBytes={msg.CharacterVisualJson?.Length ?? 0}");

                            injected = InjectIntoAssetManagerAssets(proxyGuid, clonedChar);
                            Plugin.Log.LogInfo($"[Paramulti] Step2: inject _assets[{proxyGuid:X}]={injected}");

                            if (injected)
                            {
                                ParalivesGameApiResolver.RegisterCharacterMethod?.Invoke(charMgr, new object[] { clonedChar });

                                // Mark the clone as a household member so UpdateCharacterVisualLoaded keeps
                                // its visual loaded. Without this, the visual is treated as an NPC and may
                                // be unloaded based on camera distance.
                                SetAssetField(clonedChar, "IsInHousehold", true);
                                SetAssetField(clonedChar, "IsVisibleInWorld", true);
                                SetAssetField(clonedChar, "DoNotLoadVisual", false);
                                Plugin.Log.LogInfo($"[Paramulti] Step2: marked IsInHousehold=true, IsVisibleInWorld=true");

                                var visual = ParalivesGameApiResolver.LoadCharacterVisualMethod.Invoke(
                                    charMgr, new object[] { proxyGuid });
                                Plugin.Log.LogInfo($"[Paramulti] Step2: LoadCharacterVisual({proxyGuid:X})={(visual != null ? visual.GetType().Name : "null")}");

                                if (visual != null)
                                {
                                    var visualTransform = ExtractTransform(visual);
                                    if (visualTransform != null)
                                    {
                                        StripInputComponents(visualTransform);

                                        // CRITICAL: Do NOT reparent the visual to our proxy. The game's
                                        // UpdateCharacterPositionRotationAndVisibility sets
                                        //   characterVisual.transform.localPosition = data.Position + num4
                                        // every frame, where num4 is the rig Y offset. If we reparent
                                        // to the proxy (also at world position P), the visual ends up at
                                        // P + data.Position + num4 instead of data.Position + num4.
                                        // Keep the visual parented to CharacterManager (where the game
                                        // put it) and use the visual itself as the controlled transform.
                                        entry.ControlledTransform = visualTransform;
                                        entry.GameNativeCharacter = clonedChar;
                                        entry.IsGameNative = true;

                                        // Parent the fallback cube to the visual so the debug light/sphere
                                        // follow the character as it moves through the world.
                                        if (entry.FallbackProxy != null)
                                        {
                                            entry.FallbackProxy.transform.SetParent(visualTransform, false);
                                            entry.FallbackProxy.transform.localPosition = Vector3.zero;
                                            entry.FallbackProxy.transform.localRotation = Quaternion.identity;
                                        }

                                        Plugin.Log.LogInfo($"[Paramulti] Game-native visual attached for player {msg.PlayerId} ({visualTransform.gameObject.name})");
                                    }
                                }
                                else
                                {
                                    Plugin.Log.LogInfo("[Paramulti] Step2: LoadCharacterVisual returned null after _assets injection — rolling back");
                                    RemoveFromAssetManagerAssets(proxyGuid);
                                    RemoveFromCharacterManagerList(clonedChar);
                                    injected = false;
                                }
                            }
                        }
                    }
                }
                catch (Exception gnex)
                {
                    var inner = gnex.InnerException != null ? gnex.InnerException.Message : gnex.Message;
                    Plugin.Log.LogWarning($"[Paramulti] Game-native character spawn failed (will use fallback): {inner}");
                    if (injected) RemoveFromAssetManagerAssets(proxyGuid);
                    if (clonedChar != null) RemoveFromCharacterManagerList(clonedChar);
                }
            }

            // Step 3: Fallback prefab clone — only when Step 2 didn't attach a game-native visual.
            // Otherwise we'd render the local player's sim ON TOP of the remote's correct visual,
            // making every proxy look like the local user.
            if (!entry.IsGameNative)
            {
                var prefabEntry = TryCreatePrefabClone(msg.PlayerId, msg.FullName, spawnPos);
                if (prefabEntry != null && prefabEntry.ControlledTransform != null && entry.ControlledTransform != null)
                {
                    prefabEntry.ControlledTransform.SetParent(entry.ControlledTransform, false);
                    prefabEntry.ControlledTransform.localPosition = Vector3.zero;
                    prefabEntry.ControlledTransform.localRotation = Quaternion.identity;
                    Plugin.Log.LogInfo($"[Paramulti] Parented prefab clone (fallback) to cube for player {msg.PlayerId}");
                }
            }

            lock (_lock)
            {
                _remoteCharacters[msg.PlayerId] = entry;
            }

            // Add to household so the game treats it as controllable
            if (entry.GameNativeCharacter != null)
            {
                HouseholdSyncManager.AddRemoteCharacterToHousehold(msg.CharacterGuid, entry.GameNativeCharacter);
                HouseholdSyncManager.TriggerUIRefresh();
            }

            OnRemoteCharacterCreated?.Invoke(msg.PlayerId, entry);
            Plugin.Log.LogInfo($"[Paramulti][ProxyManager] Remote character ready for player {msg.PlayerId} (gameNative={entry.IsGameNative})");
        }

        static void DisablePathfinding(object assetCharacter)
        {
            if (assetCharacter == null) return;
            try
            {
                var dataProp = assetCharacter.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(assetCharacter);
                if (data == null) return;

                var pathfindingField = data.GetType().GetField("PathfindingData");
                if (pathfindingField != null)
                {
                    var pathfinding = pathfindingField.GetValue(data);
                    if (pathfinding != null)
                    {
                        // Null out pathfinding data so the game stops driving locomotion
                        pathfindingField.SetValue(data, null);
                        Plugin.Log.LogInfo("[Paramulti] Disabled pathfinding for remote-owned character");
                    }
                }

                // Also clear current interactions to stop autonomy
                var interactionsField = data.GetType().GetField("CurrentInteractionsInQueue");
                if (interactionsField != null)
                {
                    var interactions = interactionsField.GetValue(data) as System.Collections.IList;
                    if (interactions != null)
                    {
                        interactions.Clear();
                        Plugin.Log.LogInfo("[Paramulti] Cleared autonomy interactions for remote-owned character");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] DisablePathfinding error: {ex.Message}");
            }
        }
    }
}
