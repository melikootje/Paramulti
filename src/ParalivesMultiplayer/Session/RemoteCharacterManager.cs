using System;
using System.Collections.Generic;
using System.Reflection;
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

        static GameObject _localCharacterRoot;
        static Transform _localCharacterTransform;
        static bool _localCharacterFound;

        public static Transform LocalCharacterTransform => _localCharacterTransform;
        public static bool HasLocalCharacter => _localCharacterFound;

        public static event Action<int, RemoteCharacterEntry> OnRemoteCharacterCreated;
        public static event Action<int> OnRemoteCharacterRemoved;

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[RemoteChar] Initializing RemoteCharacterManager");
            ParalivesGameApiResolver.Resolve();
            FindLocalCharacter();
        }

        public static void FindLocalCharacter()
        {
            if (_localCharacterFound) return;

            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance != null &&
                    ParalivesGameApiResolver.GetCharacterByGUIDMethod != null)
                {
                    var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                    var cmType = ParalivesGameApiResolver.CharacterManagerType;

                    var playersField = cmType.GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (playersField != null)
                    {
                        var players = playersField.GetValue(charMgr);
                        if (players is System.Collections.IEnumerable enumerable)
                        {
                            foreach (var player in enumerable)
                            {
                                var transform = ExtractTransform(player);
                                if (transform != null)
                                {
                                    _localCharacterTransform = transform;
                                    _localCharacterRoot = transform.gameObject;
                                    _localCharacterFound = true;
                                    Plugin.Log.LogInfo($"[RemoteChar] Found local character: {_localCharacterRoot.name} via CharacterManager.players");
                                    return;
                                }
                            }
                        }
                    }

                    var currentCharacterField = cmType.GetField("currentCharacter", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (currentCharacterField != null)
                    {
                        var cc = currentCharacterField.GetValue(charMgr);
                        var transform = ExtractTransform(cc);
                        if (transform != null)
                        {
                            _localCharacterTransform = transform;
                            _localCharacterRoot = transform.gameObject;
                            _localCharacterFound = true;
                            Plugin.Log.LogInfo($"[RemoteChar] Found local character: {_localCharacterRoot.name} via CharacterManager.currentCharacter");
                            return;
                        }
                    }
                }

                if (ParalivesGameApiResolver.HybridPlayerType != null)
                {
                    var hybridInstance = ParalivesGameApiResolver.HybridPlayerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                    if (hybridInstance != null)
                    {
                        var transform = ExtractTransform(hybridInstance);
                        if (transform != null)
                        {
                            _localCharacterTransform = transform;
                            _localCharacterRoot = transform.gameObject;
                            _localCharacterFound = true;
                            Plugin.Log.LogInfo($"[RemoteChar] Found local character: {_localCharacterRoot.name} via HybridPlayer.Instance");
                            return;
                        }
                    }
                }

                var playerMgr = ParalivesGameApiResolver.PlayerManagerInstance;
                if (playerMgr != null)
                {
                    var pmType = ParalivesGameApiResolver.PlayerManagerType;
                    var playersField2 = pmType.GetField("players", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (playersField2 != null)
                    {
                        var players = playersField2.GetValue(playerMgr);
                        if (players is System.Collections.IList list)
                        {
                            for (int i = 0; i < list.Count; i++)
                            {
                                var p = list[i];
                                if (p != null)
                                {
                                    var transform = ExtractTransform(p);
                                    if (transform != null)
                                    {
                                        _localCharacterTransform = transform;
                                        _localCharacterRoot = transform.gameObject;
                                        _localCharacterFound = true;
                                        Plugin.Log.LogInfo($"[RemoteChar] Found local character: {_localCharacterRoot.name} via PlayerManager.players[{i}]");
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }

                Plugin.Log.LogWarning("[RemoteChar] Could not find local character through game-native APIs");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RemoteChar] Error finding local character: {ex.Message}");
            }
        }

        public static void CreateRemoteCharacter(int playerId, string playerName)
        {
            lock (_lock)
            {
                if (_remoteCharacters.ContainsKey(playerId))
                {
                    Plugin.Log.LogWarning($"[RemoteChar] Character for player {playerId} already exists");
                    return;
                }
            }

            var entry = TryCreateGameNativeCharacter(playerId, playerName);

            if (entry == null)
            {
                Plugin.Log.LogWarning($"[RemoteChar] Game-native spawn failed for player {playerId}, using fallback proxy");
                entry = CreateFallbackProxy(playerId, playerName);
            }

            lock (_lock)
            {
                _remoteCharacters[playerId] = entry;
            }

            OnRemoteCharacterCreated?.Invoke(playerId, entry);
            Plugin.Log.LogInfo($"[RemoteChar] Created character for player {playerId} (name={playerName}, gameNative={entry.IsGameNative})");
        }

        static RemoteCharacterEntry TryCreateGameNativeCharacter(int playerId, string playerName)
        {
            try
            {
                if (ParalivesGameApiResolver.CharacterManagerInstance == null ||
                    ParalivesGameApiResolver.LoadCharacterVisualMethod == null)
                {
                    return null;
                }

                var guid = GenerateGuidForPlayer(playerId);
                Plugin.Log.LogInfo($"[RemoteChar] Attempting game-native spawn for player {playerId} with GUID={guid:X}");

                var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;

                if (ParalivesGameApiResolver.AssetCharacterDataType != null)
                {
                    var charData = Activator.CreateInstance(ParalivesGameApiResolver.AssetCharacterDataType);
                    if (charData != null)
                    {
                        var nameProp = ParalivesGameApiResolver.AssetCharacterDataType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                        if (nameProp != null && nameProp.CanWrite)
                            nameProp.SetValue(charData, $"[{playerId}] {playerName}");

                        var guidField = ParalivesGameApiResolver.AssetCharacterDataType.GetField("guid", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (guidField != null)
                            guidField.SetValue(charData, guid);

                        var posField = ParalivesGameApiResolver.AssetCharacterDataType.GetField("position", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (posField == null)
                        {
                            var posProp = ParalivesGameApiResolver.AssetCharacterDataType.GetProperty("Position", BindingFlags.Public | BindingFlags.Instance);
                            if (posProp != null && posProp.CanWrite)
                                posProp.SetValue(charData, new Vector3(0, 0, 0));
                        }
                        else
                        {
                            var posType = posField.FieldType;
                            if (posType == typeof(Vector3))
                                posField.SetValue(charData, new Vector3(0, 0, 0));
                        }

                        Plugin.Log.LogInfo($"[RemoteChar] Created AssetCharacterData for player {playerId}");
                    }
                }

                if (ParalivesGameApiResolver.LoadCharacterVisualMethod != null)
                {
                    var parameters = ParalivesGameApiResolver.LoadCharacterVisualMethod.GetParameters();
                    object result = null;

                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(ulong))
                    {
                        result = ParalivesGameApiResolver.LoadCharacterVisualMethod.Invoke(charMgr, new object[] { guid });
                    }
                    else if (parameters.Length >= 1)
                    {
                        var paramTypes = new Type[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                            paramTypes[i] = parameters[i].ParameterType;

                        var args = new object[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (paramTypes[i] == typeof(ulong))
                                args[i] = guid;
                            else if (paramTypes[i] == typeof(string))
                                args[i] = playerName;
                            else if (paramTypes[i] == typeof(Vector3))
                                args[i] = Vector3.zero;
                            else if (paramTypes[i].IsClass)
                                args[i] = null;
                        }

                        result = ParalivesGameApiResolver.LoadCharacterVisualMethod.Invoke(charMgr, args);
                    }

                    if (result != null)
                    {
                        var transform = ExtractTransform(result);
                        if (transform != null)
                        {
                            Plugin.Log.LogInfo($"[RemoteChar] Successfully spawned game-native character for player {playerId} via LoadCharacterVisual");

                            if (ParalivesGameApiResolver.RegisterCharacterMethod != null)
                            {
                                try
                                {
                                    ParalivesGameApiResolver.RegisterCharacterMethod.Invoke(charMgr, new object[] { result });
                                    Plugin.Log.LogInfo($"[RemoteChar] Registered character with CharacterManager for player {playerId}");
                                }
                                catch (Exception ex)
                                {
                                    Plugin.Log.LogWarning($"[RemoteChar] RegisterCharacter failed: {ex.Message}");
                                }
                            }

                            return new RemoteCharacterEntry
                            {
                                PlayerId = playerId,
                                CharacterGuid = guid,
                                GameNativeCharacter = result,
                                ControlledTransform = transform,
                                IsGameNative = true,
                                LastKnownPosition = transform.position,
                                LastKnownRotation = transform.rotation
                            };
                        }
                    }

                    Plugin.Log.LogWarning($"[RemoteChar] LoadCharacterVisual returned null or non-transform result for player {playerId}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RemoteChar] Game-native spawn exception for player {playerId}: {ex.Message}");
                return null;
            }
        }

        static RemoteCharacterEntry CreateFallbackProxy(int playerId, string playerName)
        {
            try
            {
                var go = new GameObject($"[Remote:{playerId}] {playerName}");
                go.tag = "Untagged";

                var transform = go.transform;
                transform.position = new Vector3(0, 0, 0);
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

                Plugin.Log.LogInfo($"[RemoteChar] Created fallback proxy for player {playerId}: {go.name}");

                return new RemoteCharacterEntry
                {
                    PlayerId = playerId,
                    CharacterGuid = GenerateGuidForPlayer(playerId),
                    ControlledTransform = transform,
                    FallbackProxy = go,
                    IsGameNative = false,
                    LastKnownPosition = Vector3.zero,
                    LastKnownRotation = Quaternion.identity
                };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RemoteChar] Failed to create fallback proxy for player {playerId}: {ex.Message}");
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

            Plugin.Log.LogInfo($"[RemoteChar] Removing character for player {playerId}");

            if (entry.IsGameNative && ParalivesGameApiResolver.CharacterManagerInstance != null)
            {
                try
                {
                    if (ParalivesGameApiResolver.GetCharacterByGUIDMethod != null)
                    {
                        var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                        var character = ParalivesGameApiResolver.GetCharacterByGUIDMethod.Invoke(charMgr, new object[] { entry.CharacterGuid });

                        if (character != null)
                        {
                            var goField = character.GetType().GetField("gameObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (goField != null)
                            {
                                var go = goField.GetValue(character) as GameObject;
                                if (go != null)
                                    DestroyGameObject(go);
                            }

                            var destroyMethod = character.GetType().GetMethod("Destroy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (destroyMethod != null)
                                destroyMethod.Invoke(character, null);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[RemoteChar] Failed to clean up game-native character for player {playerId}: {ex.Message}");
                }
            }

            if (entry.FallbackProxy != null)
            {
                DestroyGameObject(entry.FallbackProxy);
            }

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

                try
                {
                    if (entry.IsGameNative && ParalivesGameApiResolver.UpdateCharacterPositionRotationAndVisibilityMethod != null)
                    {
                        var charMgr = ParalivesGameApiResolver.CharacterManagerInstance;
                        var character = ParalivesGameApiResolver.GetCharacterByGUIDMethod?.Invoke(charMgr, new object[] { entry.CharacterGuid });

                        if (character != null)
                        {
                            ParalivesGameApiResolver.UpdateCharacterPositionRotationAndVisibilityMethod.Invoke(charMgr,
                                new object[] { character, position, rotation, true });
                            return;
                        }
                    }

                    entry.ControlledTransform.position = position;
                    entry.ControlledTransform.rotation = rotation;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[RemoteChar] Failed to apply state for player {playerId}: {ex.Message}");
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
            Plugin.Log.LogInfo("[RemoteChar] All remote characters cleaned up");
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

            var monoBehaviour = obj as MonoBehaviour;
            if (monoBehaviour != null) return monoBehaviour.transform;

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
    }
}
