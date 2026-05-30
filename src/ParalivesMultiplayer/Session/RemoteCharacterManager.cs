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
                // Path 1: Use main camera follow target (most reliable in live world)
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
                        if (followField != null)
                        {
                            var followObj = followField.GetValue(camController);
                            Transform ft = null;
                            if (followObj is Transform t) ft = t;
                            else if (followObj is GameObject g) ft = g.transform;

                            if (ft != null && IsValidLocalTransform(ft))
                            {
                                _localCharacterTransform = ft;
                                _localCharacterFound = true;
                                Plugin.Log.LogInfo($"[Paramulti] Found local character via camera follow target: {ft.gameObject.name}");
                                return;
                            }
                        }
                    }
                }

                // Path 2: Resolve runtime visual from HouseholdManager character GUIDs
                if (ParalivesGameApiResolver.HouseholdManagerInstance != null &&
                    ParalivesGameApiResolver.GetCharactersInCurrentHouseholdMethod != null &&
                    ParalivesGameApiResolver.GetLoadedCharacterVisualMethod != null)
                {
                    var hm = ParalivesGameApiResolver.HouseholdManagerInstance;
                    var chars = ParalivesGameApiResolver.GetCharactersInCurrentHouseholdMethod.Invoke(hm, null) as System.Collections.IList;
                    if (chars != null && chars.Count > 0)
                    {
                        var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                        var loadedVisMethod = ParalivesGameApiResolver.GetLoadedCharacterVisualMethod;
                        foreach (var c in chars)
                        {
                            var guidProp = c.GetType().GetProperty("GUID");
                            var guid = guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;
                            if (guid == 0) continue;

                            var runtimeVisual = loadedVisMethod.Invoke(charMgr, new object[] { guid });
                            if (runtimeVisual != null)
                            {
                                var t = ExtractTransform(runtimeVisual);
                                if (t != null && IsValidLocalTransform(t))
                                {
                                    _localCharacterTransform = t;
                                    _localCharacterFound = true;
                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via GetLoadedCharacterVisual GUID={guid:X}: {t.gameObject.name}");
                                    return;
                                }
                            }
                        }
                    }
                }

                // Path 3: CharacterManager + PlayerManager HybridPlayer via CameraCurrentCharacterFollowTarget
                if (ParalivesGameApiResolver.PlayerManagerInstance != null &&
                    ParalivesGameApiResolver.GetHybridPlayerMethod != null)
                {
                    var pm = ParalivesGameApiResolver.PlayerManagerInstance;
                    var player0 = ParalivesGameApiResolver.GetHybridPlayerMethod.Invoke(pm, new object[] { 0 });
                    if (player0 != null)
                    {
                        var followField = player0.GetType().GetField("CameraCurrentCharacterFollowTarget",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (followField != null)
                        {
                            var followGuid = (ulong)followField.GetValue(player0);
                            if (followGuid != 0)
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
                                            var guidProp = c.GetType().GetProperty("GUID");
                                            var guid = guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;
                                            if (guid == followGuid)
                                            {
                                                var visualProp = c.GetType().GetProperty("Visual");
                                                var visual = visualProp?.GetValue(c);
                                                var t = ExtractTransform(visual);
                                                if (t != null && IsValidLocalTransform(t))
                                                {
                                                    _localCharacterTransform = t;
                                                    _localCharacterFound = true;
                                                    Plugin.Log.LogInfo($"[Paramulti] Found local character via CameraCurrentCharacterFollowTarget GUID={followGuid:X}: {t.gameObject.name}");
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

        static bool IsValidLocalTransform(Transform t)
        {
            if (t == null) return false;
            var name = t.gameObject.name;
            // Reject Character Creator visual
            if (name.IndexOf("CharacterCreator", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (name.IndexOf("CreatorVisual", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            // Reject objects deep in void space (Character Creator coordinates)
            if (t.position.x > 500f || t.position.x < -500f) return false;
            if (t.position.y > 500f || t.position.y < -500f) return false;
            if (t.position.z > 500f || t.position.z < -500f) return false;
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

                // Count components before stripping
                var animators = go.GetComponentsInChildren<Animator>(true).Length;
                var skinned = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
                var meshes = go.GetComponentsInChildren<MeshRenderer>(true).Length;

                StripInputComponents(transform);
                AttachDebugMarker(go, playerId, playerName);
                // Prefab clones should be fully visible — ghost material only for game-native chars

                Plugin.Log.LogInfo($"[Paramulti] Created prefab clone for player {playerId}: {go.name} at {spawnPos} (Animator={animators}, SkinnedMesh={skinned}, MeshRenderer={meshes})");

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
                // Use Unity's built-in cube primitive — guaranteed to render correctly
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"[Remote:{playerId}] {playerName}";
                go.tag = "Untagged";

                var transform = go.transform;
                // Lift slightly above ground so it doesn't Z-fight with floor
                transform.position = new Vector3(spawnPos.x, spawnPos.y + 0.01f, spawnPos.z);
                transform.rotation = Quaternion.identity;
                // Make it human-sized and very visible
                transform.localScale = new Vector3(1.5f, 2.5f, 1.5f);

                // Remove the default collider (we don't need physics on proxy)
                var collider = go.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);

                // Get the existing renderer and change to bright unlit color
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    var color = GetPlayerColor(playerId);
                    // Try Unlit/Color first — always bright regardless of scene lighting
                    var shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Standard");
                    if (shader == null) shader = Shader.Find("Diffuse");
                    if (shader == null) shader = Shader.Find("Mobile/Diffuse");

                    if (shader != null)
                    {
                        var mat = new Material(shader);
                        mat.color = color;
                        renderer.material = mat;
                        Plugin.Log.LogInfo($"[Paramulti] Fallback proxy using shader '{shader.name}' with color {color} for player {playerId}");
                    }
                    else
                    {
                        // Fallback: just tint the existing material
                        renderer.material.color = color;
                        Plugin.Log.LogWarning($"[Paramulti] Fallback proxy: no shader found, tinting existing material for player {playerId}");
                    }
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

                entry.LastKnownPosition = position;
                entry.LastKnownRotation = rotation;

                Plugin.Log.LogInfo($"[Paramulti][Sync] Applying transform for Player {playerId}. pos={position}, rot={rotation}");

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
                if (ParalivesGameApiResolver.CharacterManagerInstance == null)
                {
                    Plugin.Log.LogWarning("[Paramulti] BuildLocalCharacterDataSync: CharacterManager instance is null");
                    return null;
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

                    var guidProp = c.GetType().GetProperty("GUID");
                    var guid = guidProp != null ? (ulong)guidProp.GetValue(c) : 0UL;

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
                        LastKnownRotation = _localCharacterTransform != null ? _localCharacterTransform.rotation.FromUnity() : new NetQuaternion(0f, 0f, 0f, 1f)
                    };

                    Plugin.Log.LogInfo($"[Paramulti] Built local character data sync: GUID={guid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}");
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
            var guid = GenerateGuidForPlayer(MultiplayerSession.LocalPlayerId);
            var pos = _localCharacterTransform != null
                ? _localCharacterTransform.position.FromUnity()
                : new NetVector3(0f, 1f, 0f);
            var rot = _localCharacterTransform != null
                ? _localCharacterTransform.rotation.FromUnity()
                : new NetQuaternion(0f, 0f, 0f, 1f);

            var msg = new ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                CharacterGuid = guid,
                FirstName = $"Player_{MultiplayerSession.LocalPlayerId}",
                FullName = $"Player_{MultiplayerSession.LocalPlayerId}",
                Age = 0f,
                SpeciesGuid = 0UL,
                CharacterModelGuid = 0UL,
                CurrentPostureGuid = 0UL,
                IsDeadOrTakenAway = false,
                LastKnownPosition = pos,
                LastKnownRotation = rot
            };

            Plugin.Log.LogInfo($"[Paramulti] Built MINIMAL character data sync: GUID={guid:X}, Name={msg.FullName}, pos={pos}");
            return msg;
        }

        static ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync BuildFallbackCharacterDataSync(ulong guid)
        {
            if (guid == 0)
                guid = GetLocalCharacterGuid();
            if (guid == 0)
                guid = GenerateGuidForPlayer(MultiplayerSession.LocalPlayerId);

            var goName = _localCharacterTransform.gameObject?.name ?? "Unknown";
            var pos = _localCharacterTransform.position.FromUnity();
            var rot = _localCharacterTransform.rotation.FromUnity();

            var msg = new ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync
            {
                PlayerId = MultiplayerSession.LocalPlayerId,
                CharacterGuid = guid,
                FirstName = $"Player_{MultiplayerSession.LocalPlayerId}",
                FullName = $"Player_{MultiplayerSession.LocalPlayerId}",
                Age = 0f,
                SpeciesGuid = 0UL,
                CharacterModelGuid = 0UL,
                CurrentPostureGuid = 0UL,
                IsDeadOrTakenAway = false,
                LastKnownPosition = pos,
                LastKnownRotation = rot
            };

            Plugin.Log.LogInfo($"[Paramulti] Built FALLBACK character data sync: GUID={guid:X}, Name={msg.FullName}, transform={goName}, pos={pos}");
            return msg;
        }

        public static void ApplyRemoteCharacterDataSync(ParalivesMultiplayer.Networking.Messages.MsgCharacterDataSync msg)
        {
            if (msg == null) return;

            Plugin.Log.LogInfo($"[Paramulti] Applying remote character data sync from player {msg.PlayerId}: GUID={msg.CharacterGuid:X}, Name={msg.FullName}, Model={msg.CharacterModelGuid:X}, pos={msg.LastKnownPosition}");

            CharacterOwnershipManager.RegisterOwnership(msg.PlayerId, msg.CharacterGuid);

            // Check if we already have this character — update position instead of recreating
            lock (_lock)
            {
                if (_remoteCharacters.TryGetValue(msg.PlayerId, out var existingEntry))
                {
                    if (existingEntry.CharacterGuid == msg.CharacterGuid)
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
            }

            // Always create the visible fallback cube FIRST — guaranteed to render
            Vector3 spawnPos = msg.LastKnownPosition.ToUnity();
            var entry = CreateFallbackProxy(msg.PlayerId, msg.FullName, spawnPos);
            if (entry != null)
            {
                entry.CharacterGuid = msg.CharacterGuid;
                entry.ControlledTransform.position = spawnPos;
                entry.ControlledTransform.rotation = msg.LastKnownRotation.ToUnity();
            }

            // Optionally try to enhance with actual character model (prefab clone)
            // Parent it to the fallback cube so the cube remains as backup visibility
            var prefabEntry = TryCreatePrefabClone(msg.PlayerId, msg.FullName, spawnPos);
            if (prefabEntry != null && prefabEntry.ControlledTransform != null)
            {
                // Parent the character model to our visible cube so they move together
                if (entry != null && entry.ControlledTransform != null)
                {
                    prefabEntry.ControlledTransform.SetParent(entry.ControlledTransform, false);
                    prefabEntry.ControlledTransform.localPosition = Vector3.zero;
                    prefabEntry.ControlledTransform.localRotation = Quaternion.identity;
                    Plugin.Log.LogInfo($"[Paramulti] Parented prefab clone to fallback cube for player {msg.PlayerId}");
                }
                else
                {
                    // No fallback cube exists, use the prefab clone directly
                    entry = prefabEntry;
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
