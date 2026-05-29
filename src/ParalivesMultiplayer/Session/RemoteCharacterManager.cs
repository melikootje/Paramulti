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
        const float FindRetryInterval = 5f; // seconds between retry attempts

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

        public static void FindLocalCharacter()
        {
            if (_localCharacterFound) return;

            ParalivesGameApiResolver.Resolve();

            // Managers not ready yet - wait silently until they are
            if (ParalivesGameApiResolver.CharacterManagerInstance == null) return;

            // Throttle: only retry finding character every FindRetryInterval seconds
            if (Time.time - _lastFindAttemptTime < FindRetryInterval) return;
            _lastFindAttemptTime = Time.time;

            try
            {
                // Path 1: CharacterManager + PlayerManager HybridPlayer via GetHybridPlayer(index)
                if (ParalivesGameApiResolver.CharacterManagerInstance != null &&
                    ParalivesGameApiResolver.PlayerManagerInstance != null &&
                    ParalivesGameApiResolver.GetHybridPlayerMethod != null)
                {
                    var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                    var cmType = ParalivesGameApiResolver.CharacterManagerType;
                    var charsProp = cmType.GetProperty("Characters");
                    if (charsProp != null)
                    {
                        var chars = charsProp.GetValue(charMgr) as System.Collections.IList;
                        if (chars != null && chars.Count > 0)
                        {
                            var pm = ParalivesGameApiResolver.PlayerManagerInstance;

                            // Use GetHybridPlayer(0) to get the first player
                            var player0 = ParalivesGameApiResolver.GetHybridPlayerMethod.Invoke(pm, new object[] { 0 });
                            if (player0 != null)
                            {
                                Plugin.Log.LogInfo($"[Paramulti] Player0 type: {player0.GetType().FullName}");

                                // Find CameraCurrentCharacterFollowTarget field on HybridPlayer
                                var followField = player0.GetType().GetField("CameraCurrentCharacterFollowTarget",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (followField != null)
                                {
                                    var followGuid = (ulong)followField.GetValue(player0);
                                    Plugin.Log.LogInfo($"[Paramulti] CameraCurrentCharacterFollowTarget GUID={followGuid:X}");
                                    if (followGuid != 0)
                                    {
                                        foreach (var c in chars)
                                        {
                                            var guidProp = c.GetType().GetProperty("GUID");
                                            var guid = guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;
                                            if (guid == followGuid)
                                            {
                                                Plugin.Log.LogInfo($"[Paramulti] MATCHED local character AssetCharacter! Dumping all members...");
                                                DumpAssetCharacterMembers(c);
                                                var visualProp = c.GetType().GetProperty("Visual");
                                                var visual = visualProp?.GetValue(c);
                                                var t = ExtractTransform(visual);
                                                if (t != null)
                                                {
                                                    _localCharacterTransform = t;
                                                    _localCharacterFound = true;
                                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via CameraCurrentCharacterFollowTarget GUID={followGuid:X}");
                                                    return;
                                                }
                                            }
                                        }
                                    }
                                }

                                Plugin.Log.LogInfo("[Paramulti] followGuid=0, trying CharacterVisualInEdition from HybridPlayer...");
                                // CharacterVisualInEdition is a CharacterVisual (runtime scene component) with a real Transform
                                var cveField = player0.GetType().GetField("CharacterVisualInEdition",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (cveField != null)
                                {
                                    var cve = cveField.GetValue(player0);
                                    if (cve != null)
                                    {
                                        Plugin.Log.LogInfo($"[Paramulti] CharacterVisualInEdition type: {cve.GetType().FullName}");
                                        var t = ExtractTransform(cve);
                                        if (t != null)
                                        {
                                            _localCharacterTransform = t;
                                            _localCharacterFound = true;
                                            Plugin.Log.LogInfo($"[Paramulti] Found local character via CharacterVisualInEdition: {t.gameObject.name}");
                                            return;
                                        }
                                        else
                                        {
                                            Plugin.Log.LogInfo("[Paramulti] CharacterVisualInEdition has no extractable Transform");
                                            DumpTypeMembersOnce(cve);
                                        }
                                    }
                                    else
                                    {
                                        Plugin.Log.LogInfo("[Paramulti] CharacterVisualInEdition is null");
                                    }
                                }
                                else
                                {
                                    Plugin.Log.LogInfo("[Paramulti] CharacterVisualInEdition field not found on HybridPlayer");
                                }
                            }
                            else
                            {
                                Plugin.Log.LogInfo("[Paramulti] GetHybridPlayer(0) returned null");
                            }
                        }
                    }
                }

                // Path 2: HouseholdManager current household characters
                if (ParalivesGameApiResolver.HouseholdManagerInstance != null)
                {
                    var hm = ParalivesGameApiResolver.HouseholdManagerInstance;
                    var getCharsMethod = ParalivesGameApiResolver.GetCharactersInCurrentHouseholdMethod;
                    if (getCharsMethod != null)
                    {
                        var chars = getCharsMethod.Invoke(hm, null) as System.Collections.IList;
                        if (chars != null && chars.Count > 0)
                        {
                            foreach (var c in chars)
                            {
                                var visualProp = c.GetType().GetProperty("Visual");
                                var visual = visualProp?.GetValue(c);
                                var t = ExtractTransform(visual);
                                if (t != null)
                                {
                                    _localCharacterTransform = t;
                                    _localCharacterFound = true;
                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via HouseholdManager: {t.gameObject.name}");
                                    return;
                                }
                            }
                        }
                    }
                }

                // Debug: log what we're finding at each step
                Plugin.Log.LogInfo("[Paramulti] Character search failed — diagnostics:");
                var charMgr2 = ParalivesGameApiResolver.CharacterManagerInstance;
                var cmType2 = ParalivesGameApiResolver.CharacterManagerType;
                var charsProp2 = cmType2.GetProperty("Characters");
                Plugin.Log.LogInfo($"[Paramulti]   Characters property: {(charsProp2 != null ? "found" : "null")}");
                if (charsProp2 != null)
                {
                    var chars2 = charsProp2.GetValue(charMgr2) as System.Collections.IList;
                    Plugin.Log.LogInfo($"[Paramulti]   Characters list: {(chars2 != null ? chars2.Count.ToString() : "null")}");
                    if (chars2 != null && chars2.Count > 0)
                    {
                        foreach (var c in chars2)
                        {
                            var visualProp2 = c.GetType().GetProperty("Visual");
                            var visual2 = visualProp2?.GetValue(c);
                            var t2 = ExtractTransform(visual2);
                            Plugin.Log.LogInfo($"[Paramulti]     char visualType={visual2?.GetType().Name ?? "null"}, extracted={t2 != null}");
                        }
                    }
                }
                Plugin.Log.LogWarning("[Paramulti] Could not find local character through game-native APIs");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] Error finding local character: {ex.Message}");
            }
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

        static ulong GetLocalCharacterModelGuid()
        {
            if (_localCharacterTransform == null || ParalivesGameApiResolver.CharacterManagerInstance == null)
                return 0;

            try
            {
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
                        var dataProp = c.GetType().GetProperty("Data");
                        var data = dataProp?.GetValue(c);
                        if (data != null)
                        {
                            var modelField = data.GetType().GetField("CurrentCharacterModelGUID");
                            if (modelField != null)
                                return (ulong)modelField.GetValue(data);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Paramulti] GetLocalCharacterModelGuid error: {ex.Message}");
            }
            return 0;
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

                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var createMethod = ParalivesGameApiResolver.CreateCharacterByModelGUIDMethod;
                if (createMethod == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] CreateCharacterByModelGUID not resolved");
                    return null;
                }

                ulong modelGuid = GetLocalCharacterModelGuid();
                if (modelGuid == 0)
                {
                    Plugin.Log.LogWarning("[Paramulti] No local character model GUID; cannot create game-native character");
                    return null;
                }

                Plugin.Log.LogInfo($"[Paramulti] Creating game-native character for player {playerId} with model GUID={modelGuid:X}");

                var newAssetChar = createMethod.Invoke(charMgr, new object[] { modelGuid });
                if (newAssetChar == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] CreateCharacterByModelGUID returned null");
                    return null;
                }

                // Set name
                var dataProp = newAssetChar.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(newAssetChar);
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
                    regMethod.Invoke(charMgr, new object[] { newAssetChar });
                    Plugin.Log.LogInfo($"[Paramulti] Registered game-native character for player {playerId}");
                }

                // Get GUID
                var guidProp = newAssetChar.GetType().GetProperty("GUID");
                var guid = guidProp != null ? (ulong)guidProp.GetValue(newAssetChar) : GenerateGuidForPlayer(playerId);

                // Load visual
                var loadVisualMethod = ParalivesGameApiResolver.LoadCharacterVisualMethod;
                if (loadVisualMethod == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] LoadCharacterVisual not resolved");
                    return null;
                }

                var visual = loadVisualMethod.Invoke(charMgr, new object[] { guid });
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

                Plugin.Log.LogInfo($"[Paramulti] Successfully spawned game-native character for player {playerId} (GUID={guid:X})");

                return new RemoteCharacterEntry
                {
                    PlayerId = playerId,
                    CharacterGuid = guid,
                    GameNativeCharacter = newAssetChar,
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

        static RemoteCharacterEntry TryCreatePrefabClone(int playerId, string playerName, Vector3 spawnPos)
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null)
                    return null;

                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var cmType = ParalivesGameApiResolver.CharacterManagerType;
                var prefabField = cmType.GetField("CharacterPrefab");
                if (prefabField == null) return null;

                var prefab = prefabField.GetValue(charMgr) as Component;
                if (prefab == null) return null;

                var go = UnityEngine.Object.Instantiate(prefab.gameObject);
                go.name = $"[Remote:{playerId}] {playerName}";
                var transform = go.transform;
                transform.position = spawnPos;
                transform.rotation = Quaternion.identity;

                StripInputComponents(transform);

                Plugin.Log.LogInfo($"[Paramulti] Created prefab clone for player {playerId}: {go.name}");

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
                Plugin.Log.LogWarning($"[Paramulti] Prefab clone failed for player {playerId}: {ex.Message}");
                return null;
            }
        }

        static RemoteCharacterEntry CreateFallbackProxy(int playerId, string playerName, Vector3 spawnPos)
        {
            try
            {
                var go = new GameObject($"[Remote:{playerId}] {playerName}");
                go.tag = "Untagged";

                var transform = go.transform;
                transform.position = spawnPos;
                transform.rotation = Quaternion.identity;

                var capsuleType = Type.GetType("UnityEngine.CapsuleCollider, UnityEngine.PhysicsModule") ?? Type.GetType("CapsuleCollider, UnityEngine");
                if (capsuleType != null)
                {
                    try { go.AddComponent(capsuleType); } catch { }
                }

                var renderer = go.AddComponent<MeshRenderer>();
                var filter = go.AddComponent<MeshFilter>();
                filter.mesh = CreateSimpleMesh();

                var mat = new Material(Shader.Find("Standard"));
                mat.color = GetPlayerColor(playerId);
                renderer.material = mat;

                StripInputComponents(transform);

                Plugin.Log.LogInfo($"[Paramulti] Created fallback proxy for player {playerId}: {go.name}");

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

                Vector3[] vertices = new Vector3[8];
                int[] triangles = new int[36];

                float r = 0.4f, h = 1.0f;
                float[] angles = new float[] { 0f, Mathf.PI / 2f, Mathf.PI, Mathf.PI * 3f / 2f };

                for (int i = 0; i < 4; i++)
                {
                    float a = angles[i];
                    vertices[i] = new Vector3(Mathf.Cos(a) * r, -h, Mathf.Sin(a) * r);
                    vertices[i + 4] = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
                }

                int[] order = { 0, 1, 2, 3 };
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    int baseIdx = i * 6;

                    triangles[baseIdx] = order[i];
                    triangles[baseIdx + 1] = next;
                    triangles[baseIdx + 2] = next + 4;
                    triangles[baseIdx + 3] = order[i];
                    triangles[baseIdx + 4] = next + 4;
                    triangles[baseIdx + 5] = order[i] + 4;
                }

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
                    var delMethod = ParalivesGameApiResolver.DeleteCharacterMethod;
                    if (delMethod != null)
                    {
                        delMethod.Invoke(ParalivesGameApiResolver.CharacterManagerInstance,
                            new object[] { 0, entry.CharacterGuid, false });
                        Plugin.Log.LogInfo($"[Paramulti] Deleted game-native character for player {playerId}");
                    }
                    else
                    {
                        DestroyGameObject(entry.ControlledTransform?.gameObject);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Paramulti] Failed to clean up game-native character for player {playerId}: {ex.Message}");
                    DestroyGameObject(entry.ControlledTransform?.gameObject);
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

        public static void ApplyRemoteState(int playerId, Vector3 position, Quaternion rotation)
        {
            lock (_lock)
            {
                if (!_remoteCharacters.TryGetValue(playerId, out var entry)) return;
                if (entry.ControlledTransform == null) return;

                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;

                Plugin.Log.LogDebug($"[Paramulti][Sync] Applying transform for Player {playerId}. pos={position}, rot={rotation}");

                try
                {
                    if (entry.IsGameNative && entry.GameNativeCharacter != null)
                    {
                        entry.ControlledTransform.position = position;
                        entry.ControlledTransform.rotation = rotation;

                        // Mirror into character data so the game knows where it is
                        var dataProp = entry.GameNativeCharacter.GetType().GetProperty("Data");
                        var data = dataProp?.GetValue(entry.GameNativeCharacter);
                        if (data != null)
                        {
                            var lastPosField = data.GetType().GetField("LastPositionUsedForZoneObject");
                            lastPosField?.SetValue(data, position);
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

            var inputComponentNames = new string[]
            {
                "PlayerInput", "InputManager", "CharacterController",
                "Rigidbody", "Animator", "NavMeshAgent",
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
                Plugin.Log.LogInfo($"[Paramulti] Stripped {stripped} input/control components from remote character");
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
                                Plugin.Log.LogInfo($"[Paramulti] AssetCharacterVisual.Data.CurrentCharacterModelGUID={modelGuid:X}");

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
                                            Plugin.Log.LogInfo($"[Paramulti] GetCharacterByGUID({modelGuid:X}) = {assetChar.GetType().FullName}");

                                            // Get the visual from this AssetCharacter
                                            var visProp = assetChar.GetType().GetProperty("Visual");
                                            var runtimeVisual = visProp?.GetValue(assetChar);
                                            Plugin.Log.LogInfo($"[Paramulti] AssetCharacter.Visual = {runtimeVisual?.GetType().FullName ?? "null"}");
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
                                        Plugin.Log.LogInfo($"[Paramulti] GetLoadedCharacterVisual({modelGuid:X}) = {runtimeVisual?.GetType().FullName ?? "null"}");
                                        if (runtimeVisual != null)
                                        {
                                            var t2 = ExtractTransform(runtimeVisual);
                                            if (t2 != null) return t2;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Plugin.Log.LogInfo($"[Paramulti] No CurrentCharacterModelGUID field on {dataType.FullName}");
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
                                        Plugin.Log.LogInfo($"[Paramulti] Found parent AssetCharacter by reference match");
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
                                        Plugin.Log.LogInfo($"[Paramulti] AssetCharacter GUID={charGUID:X}");
                                        if (charGUID != 0)
                                        {
                                            var loadedMethod = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                                            if (loadedMethod != null)
                                            {
                                                var rv = loadedMethod.Invoke(charMgr, new object[] { charGUID });
                                                Plugin.Log.LogInfo($"[Paramulti] GetLoadedCharacterVisual({charGUID:X}) = {rv?.GetType().FullName ?? "null"}");
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
                catch (Exception ex)
                {
                    Plugin.Log.LogInfo($"[Paramulti] AssetCharacterVisual extraction failed: {ex.Message}");
                }

                DumpTypeMembersOnce(obj);
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

            // Last-chance diagnostic: dump all members of unknown types once
            DumpTypeMembersOnce(obj);

            return null;
        }

        static void DumpAssetCharacterMembers(object assetCharacter)
        {
            if (assetCharacter == null) return;
            var t = assetCharacter.GetType();
            try
            {
                Plugin.Log.LogInfo($"[Paramulti] DUMP AssetCharacter type={t.FullName}:");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try
                    {
                        var v = p.GetValue(assetCharacter);
                        Plugin.Log.LogInfo($"[Paramulti]   prop {p.Name}: {v?.GetType().Name ?? "null"} = {v?.ToString() ?? "null"}");
                    }
                    catch { }
                }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    try
                    {
                        var v = f.GetValue(assetCharacter);
                        Plugin.Log.LogInfo($"[Paramulti]   field {f.Name}: {v?.GetType().Name ?? "null"} = {v?.ToString() ?? "null"}");
                    }
                    catch { }
                }

                // Also dump the Visual if present
                var visualProp = t.GetProperty("Visual");
                if (visualProp != null)
                {
                    var visual = visualProp.GetValue(assetCharacter);
                    if (visual != null)
                    {
                        DumpTypeMembersOnce(visual);
                    }
                }

                // Also dump the Data if present
                var dataProp2 = t.GetProperty("Data");
                if (dataProp2 != null)
                {
                    var data = dataProp2.GetValue(assetCharacter);
                    if (data != null)
                    {
                        DumpTypeMembersOnce(data);
                    }
                }
            }
            catch { }
        }

        static HashSet<string> _dumpedTypes = new HashSet<string>();
        static object _dumpLock = new object();

        static void DumpTypeMembersOnce(object obj)
        {
            if (obj == null) return;
            var t = obj.GetType();
            lock (_dumpLock)
            {
                if (_dumpedTypes.Contains(t.FullName)) return;
                _dumpedTypes.Add(t.FullName);
            }

            try
            {
                Plugin.Log.LogInfo($"[Paramulti] Dumping type {t.FullName} members:");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    try { var v = p.GetValue(obj); Plugin.Log.LogInfo($"[Paramulti]   prop {p.Name}: {v?.GetType().Name ?? "null"} = {v?.ToString() ?? "null"}"); } catch { }
                }
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
                {
                    try { var v = f.GetValue(obj); Plugin.Log.LogInfo($"[Paramulti]   field {f.Name}: {v?.GetType().Name ?? "null"} = {v?.ToString() ?? "null"}"); } catch { }
                }
            }
            catch { }
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
                        var guidProp = c.GetType().GetProperty("GUID");
                        return guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;
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

        public static ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync BuildLocalCharacterDataSync()
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null) return null;
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                var cmType = ParalivesGameApiResolver.CharacterManagerType;
                var charsProp = cmType.GetProperty("Characters");
                if (charsProp == null) return null;
                var chars = charsProp.GetValue(charMgr) as System.Collections.IList;
                if (chars == null) return null;

                foreach (var c in chars)
                {
                    var visualProp = c.GetType().GetProperty("Visual");
                    var visual = visualProp?.GetValue(c);
                    var t = ExtractTransform(visual);
                    if (t != _localCharacterTransform) continue;

                    var guidProp = c.GetType().GetProperty("GUID");
                    var guid = guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;

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
                        LastKnownRotation = _localCharacterTransform != null ? _localCharacterTransform.rotation.FromUnity() : new NetQuaternion(0f, 0f, 0f, 1f)
                    };

                    Plugin.Log.LogInfo($"[Paramulti] Built local character data sync: GUID={guid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}");
                    return msg;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] BuildLocalCharacterDataSync error: {ex.Message}");
            }
            return null;
        }

        public static void ApplyRemoteCharacterDataSync(ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync msg)
        {
            if (msg == null) return;

            Plugin.Log.LogInfo($"[Paramulti] Applying remote character data sync from player {msg.PlayerId}: GUID={msg.CharacterGuid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}");

            CharacterOwnershipManager.RegisterOwnership(msg.PlayerId, msg.CharacterGuid);

            // Check if we already have this character
            lock (_lock)
            {
                foreach (var kv in _remoteCharacters)
                {
                    if (kv.Value.CharacterGuid == msg.CharacterGuid)
                    {
                        Plugin.Log.LogInfo($"[Paramulti] Remote character GUID={msg.CharacterGuid:X} already exists, skipping creation");
                        return;
                    }
                }
            }

            // Create game-native character using the remote player's model
            Vector3 spawnPos = msg.LastKnownPosition.ToUnity();
            var entry = TryCreateRemoteGameNativeCharacter(msg.PlayerId, msg.CharacterGuid, msg.CharacterModelGuid, msg.FullName, spawnPos);

            if (entry == null)
            {
                Plugin.Log.LogWarning($"[Paramulti] Game-native creation failed for remote player {msg.PlayerId}, trying prefab fallback");
                entry = TryCreatePrefabClone(msg.PlayerId, msg.FullName, spawnPos);
                if (entry != null)
                {
                    entry.CharacterGuid = msg.CharacterGuid;
                    entry.ControlledTransform.position = spawnPos;
                    entry.ControlledTransform.rotation = msg.LastKnownRotation.ToUnity();
                }
            }

            if (entry == null)
            {
                Plugin.Log.LogWarning($"[Paramulti] Prefab fallback failed for remote player {msg.PlayerId}, using basic fallback");
                entry = CreateFallbackProxy(msg.PlayerId, msg.FullName, spawnPos);
                if (entry != null)
                {
                    entry.CharacterGuid = msg.CharacterGuid;
                    entry.ControlledTransform.position = spawnPos;
                    entry.ControlledTransform.rotation = msg.LastKnownRotation.ToUnity();
                }
            }

            if (entry == null)
            {
                Plugin.Log.LogError($"[Paramulti] CRITICAL: Could not create remote character for player {msg.PlayerId}");
                return;
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

        static RemoteCharacterEntry TryCreateRemoteGameNativeCharacter(int playerId, ulong characterGuid, ulong modelGuid, string name, Vector3 spawnPos)
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null) return null;
                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;

                var createMethod = ParalivesGameApiResolver.CreateCharacterByModelGUIDMethod;
                if (createMethod == null) return null;

                ulong effectiveModel = modelGuid != 0 ? modelGuid : GetLocalCharacterModelGuid();
                if (effectiveModel == 0)
                {
                    Plugin.Log.LogWarning("[Paramulti] No model GUID available for remote character creation");
                    return null;
                }

                Plugin.Log.LogInfo($"[Paramulti] Creating remote game-native character for player {playerId} with model GUID={effectiveModel:X}");

                var newAssetChar = createMethod.Invoke(charMgr, new object[] { effectiveModel });
                if (newAssetChar == null) return null;

                // Override the generated GUID with the remote player's known GUID if possible
                var guidField = newAssetChar.GetType().GetField("GUID");
                if (guidField != null)
                    guidField.SetValue(newAssetChar, characterGuid);

                // Set name
                var dataProp = newAssetChar.GetType().GetProperty("Data");
                var data = dataProp?.GetValue(newAssetChar);
                if (data != null)
                {
                    var firstNameField = data.GetType().GetField("FirstName");
                    firstNameField?.SetValue(data, name);
                    var fullNameField = data.GetType().GetField("FullName");
                    fullNameField?.SetValue(data, name);
                }

                // Register
                var regMethod = ParalivesGameApiResolver.RegisterCharacterMethod;
                if (regMethod != null)
                {
                    try { regMethod.Invoke(charMgr, new object[] { newAssetChar }); } catch { }
                }

                // Load visual
                var loadVisualMethod = ParalivesGameApiResolver.LoadCharacterVisualMethod;
                if (loadVisualMethod == null) return null;

                var visual = loadVisualMethod.Invoke(charMgr, new object[] { characterGuid });
                if (visual == null) return null;

                var transform = ExtractTransform(visual);
                if (transform == null) return null;

                transform.position = spawnPos;
                transform.rotation = Quaternion.identity;

                StripInputComponents(transform);
                DisablePathfinding(newAssetChar);

                Plugin.Log.LogInfo($"[Paramulti] Remote game-native character created for player {playerId} (GUID={characterGuid:X})");

                return new RemoteCharacterEntry
                {
                    PlayerId = playerId,
                    CharacterGuid = characterGuid,
                    GameNativeCharacter = newAssetChar,
                    ControlledTransform = transform,
                    IsGameNative = true,
                    LastKnownPosition = spawnPos,
                    LastKnownRotation = Quaternion.identity
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Paramulti] Remote game-native spawn exception for player {playerId}: {ex.Message}");
                return null;
            }
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
