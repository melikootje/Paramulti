using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Session;
using UnityEngine;

namespace ParalivesMultiplayer.Patches
{
    static class BuildModePatches
    {
        static bool _enabled = true;
        static float _lastSyncTime;
        const float SyncInterval = 0.05f;

        static bool _inBuildMode;
        static int _buildSequence;

        public static void Apply(Harmony harmony)
        {
            PatchBuildManager(harmony);
            PatchBuildableObject(harmony);
            PatchSceneObjectPlacement(harmony);
        }

        static void PatchBuildManager(Harmony harmony)
        {
            try
            {
                Type buildMgrType = null;
                string[] candidateNames = { "BuildManager", "Builder", "ConstructionManager", "EditModeController" };

                foreach (var candidate in candidateNames)
                {
                    buildMgrType = ParalivesGameApiResolver.FindTypeBySimpleName(candidate);
                    if (buildMgrType != null) break;
                }

                if (buildMgrType == null)
                {
                    buildMgrType = ParalivesGameApiResolver.FindTypeByPartialName("Build");
                    if (buildMgrType != null)
                        PatchLogger.Log($"BuildManager exact type not found, using partial match: {buildMgrType.FullName}");
                }

                if (buildMgrType == null)
                {
                    PatchLogger.LogWarning("BuildManager type not found, skipping build mode patches.");
                    return;
                }

                string[] methodNames = { "PlaceObject", "RemoveObject", "OnObjectPlaced", "OnObjectRemoved", "Place", "Destroy", "AddObject", "DeleteObject" };

                foreach (var methodName in methodNames)
                {
                    var method = AccessTools.Method(buildMgrType, methodName);
                    if (method != null)
                    {
                        PatchLogger.SafePatch(harmony, method,
                            new HarmonyMethod(typeof(BuildModePatches), nameof(BuildActionPostfix)),
                            $"BuildManager.{methodName}");
                    }
                }

                var toggleMethod = AccessTools.Method(buildMgrType, "ToggleMode");
                if (toggleMethod == null)
                    toggleMethod = AccessTools.Method(buildMgrType, "SetMode");
                if (toggleMethod == null)
                    toggleMethod = AccessTools.Method(buildMgrType, "EnterEditMode");

                if (toggleMethod != null)
                {
                    PatchLogger.SafePatch(harmony, toggleMethod,
                        new HarmonyMethod(typeof(BuildModePatches), nameof(BuildModeTogglePostfix)),
                        $"BuildManager.{toggleMethod.Name}");
                }

                var isBuildModeProp = AccessTools.Property(buildMgrType, "IsBuildMode");
                if (isBuildModeProp == null)
                    isBuildModeProp = AccessTools.Property(buildMgrType, "EditMode");
                if (isBuildModeProp == null)
                    isBuildModeProp = AccessTools.Property(buildMgrType, "IsEditing");

                if (isBuildModeProp != null)
                {
                    PatchLogger.SafePatch(harmony, AccessTools.PropertyGetter(buildMgrType, isBuildModeProp.Name),
                        new HarmonyMethod(typeof(BuildModePatches), nameof(IsBuildModeGetterPostfix)),
                        $"BuildManager.{isBuildModeProp.Name}.get");
                }

                PatchLogger.Log($"Patched BuildManager: {buildMgrType.FullName}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply build manager patches: {ex.Message}");
            }
        }

        static void PatchBuildableObject(Harmony harmony)
        {
            try
            {
                Type buildObjType = null;
                string[] candidates = { "BuildableObject", "ConstructibleObject", "PlaceableObject" };

                foreach (var candidate in candidates)
                {
                    buildObjType = ParalivesGameApiResolver.FindTypeBySimpleName(candidate);
                    if (buildObjType != null) break;
                }

                if (buildObjType == null)
                {
                    PatchLogger.LogWarning("BuildableObject type not found, skipping object patches.");
                    return;
                }

                var onPlace = AccessTools.Method(buildObjType, "OnPlace");
                if (onPlace != null)
                {
                    PatchLogger.SafePatch(harmony, onPlace,
                        new HarmonyMethod(typeof(BuildModePatches), nameof(ObjectPlacedPostfix)),
                        $"BuildableObject.OnPlace");
                }

                var onDestroy = AccessTools.Method(buildObjType, "OnDestroy");
                if (onDestroy != null)
                {
                    PatchLogger.SafePatch(harmony, onDestroy,
                        new HarmonyMethod(typeof(BuildModePatches), nameof(ObjectDestroyedPostfix)),
                        $"BuildableObject.OnDestroy");
                }

                var onTransformChanged = AccessTools.Method(buildObjType, "OnTransformChanged");
                if (onTransformChanged == null)
                    onTransformChanged = AccessTools.Method(buildObjType, "UpdateTransform");
                if (onTransformChanged != null)
                {
                    PatchLogger.SafePatch(harmony, onTransformChanged,
                        new HarmonyMethod(typeof(BuildModePatches), nameof(ObjectTransformChangedPostfix)),
                        $"BuildableObject.{onTransformChanged.Name}");
                }

                PatchLogger.Log($"Patched BuildableObject: {buildObjType.FullName}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply buildable object patches: {ex.Message}");
            }
        }

        static void PatchSceneObjectPlacement(Harmony harmony)
        {
            try
            {
                Type placementType = null;
                string[] candidates = { "ObjectPlacer", "PlacementSystem", "GridPlacer" };

                foreach (var candidate in candidates)
                {
                    placementType = ParalivesGameApiResolver.FindTypeBySimpleName(candidate);
                    if (placementType != null) break;
                }

                if (placementType == null)
                {
                    PatchLogger.LogWarning("ObjectPlacer type not found, skipping placement patches.");
                    return;
                }

                var placeMethod = AccessTools.Method(placementType, "Place");
                if (placeMethod != null)
                {
                    PatchLogger.SafePatch(harmony, placeMethod,
                        new HarmonyMethod(typeof(BuildModePatches), nameof(PlacementPostfix)),
                        $"ObjectPlacer.Place");
                }

                PatchLogger.Log($"Patched ObjectPlacer: {placementType.FullName}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"Failed to apply placement patches: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void BuildActionPostfix(object __Instance, object[] __args = null)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                var net = TcpNetworkManager.Instance;
                if (net == null) return;

                var extracted = ExtractObjectData(__Instance, __args);
                uint seqId = (uint)System.Threading.Interlocked.Increment(ref _buildSequence);

                var evtMsg = new MsgBuildModeEvent
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    Tick = MultiplayerSession.Tick,
                    EventType = BuildEventType.ObjectPlaced,
                    EntityId = seqId,
                    ObjectTypeId = extracted.TypeId,
                    Position = extracted.Position.FromUnity(),
                    Rotation = extracted.Rotation.FromUnity(),
                    Scale = extracted.Scale.FromUnity(),
                    StyleName = extracted.StyleName
                };

                if (BuildSyncManager.Enabled)
                {
                    BuildSyncManager.ValidateAndApply(evtMsg);
                }

                if (MultiplayerSession.IsHost)
                {
                    net.SendToAllClients(evtMsg);
                }
                else
                {
                    net.SendToHost(evtMsg);
                }

                EntitySyncManager.RegisterSpawn(seqId, extracted.TypeId, extracted.Position, extracted.Rotation, extracted.Scale, MultiplayerSession.LocalPlayerId);
                PatchLogger.LogDebug($"Build action: type={extracted.TypeId}, pos={extracted.Position}, seq={seqId}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"BuildActionPostfix error: {ex.Message}");
            }
        }

        static ExtractedObjectData ExtractObjectData(object instance, object[] args)
        {
            var result = new ExtractedObjectData
            {
                TypeId = "unknown",
                Position = Vector3.zero,
                Rotation = Quaternion.identity,
                Scale = Vector3.one,
                StyleName = ""
            };

            if (instance == null) return result;

            GameObject targetGo = null;
            Component targetComp = null;

            if (instance is GameObject go)
            {
                targetGo = go;
            }
            else if (instance is Component comp)
            {
                targetComp = comp;
                targetGo = comp.gameObject;
            }
            else
            {
                Type instType = instance.GetType();

                var goProp = instType.GetProperty("gameObject");
                if (goProp != null) targetGo = goProp.GetValue(instance, null) as GameObject;

                if (targetGo == null)
                {
                    var transformProp = instType.GetProperty("Transform") ?? instType.GetProperty("transform");
                    if (transformProp != null)
                    {
                        var tr = transformProp.GetValue(instance, null);
                        if (tr is Transform t)
                        {
                            result.Position = (Vector3)t.position;
                            result.Rotation = (Quaternion)t.rotation;
                            result.Scale = (Vector3)t.localScale;
                        }
                    }

                    var nameProp = instType.GetProperty("Name") ?? instType.GetProperty("name");
                    if (nameProp != null) result.TypeId = nameProp.GetValue(instance, null)?.ToString() ?? "unknown";

                    var posProp = instType.GetProperty("Position") ?? instType.GetProperty("position");
                    if (posProp != null && result.Position == Vector3.zero)
                        result.Position = (Vector3)(posProp.GetValue(instance, null) ?? Vector3.zero);

                    var rotProp = instType.GetProperty("Rotation") ?? instType.GetProperty("rotation");
                    if (rotProp != null && result.Rotation == Quaternion.identity)
                        result.Rotation = (Quaternion)(rotProp.GetValue(instance, null) ?? Quaternion.identity);

                    var scaleProp = instType.GetProperty("Scale") ?? instType.GetProperty("scale");
                    if (scaleProp != null && result.Scale == Vector3.one)
                        result.Scale = (Vector3)(scaleProp.GetValue(instance, null) ?? Vector3.one);

                    var styleProp = instType.GetProperty("StyleName") ?? instType.GetProperty("styleName") ?? instType.GetProperty("Style");
                    if (styleProp != null) result.StyleName = styleProp.GetValue(instance, null)?.ToString() ?? "";
                }
            }

            if (targetGo != null)
            {
                result.TypeId = targetGo.name;
                result.Position = targetGo.transform.position;
                result.Rotation = targetGo.transform.rotation;
                result.Scale = targetGo.transform.localScale;
            }
            else if (targetComp != null && targetComp.gameObject != null)
            {
                result.TypeId = targetComp.gameObject.name;
                result.Position = targetComp.transform.position;
                result.Rotation = targetComp.transform.rotation;
                result.Scale = targetComp.transform.localScale;
            }

            if (args != null)
            {
                foreach (var arg in args)
                {
                    if (arg is GameObject argGo && (targetGo == null || result.TypeId == "unknown"))
                    {
                        result.TypeId = argGo.name;
                        result.Position = argGo.transform.position;
                        result.Rotation = argGo.transform.rotation;
                        result.Scale = argGo.transform.localScale;
                    }
                    else if (arg is Component argComp && argComp.gameObject != null)
                    {
                        if (result.Position == Vector3.zero || result.TypeId == "unknown")
                        {
                            result.TypeId = argComp.gameObject.name;
                            result.Position = argComp.transform.position;
                            result.Rotation = argComp.transform.rotation;
                            result.Scale = argComp.transform.localScale;
                        }
                    }
                }
            }

            return result;
        }

        struct ExtractedObjectData
        {
            public string TypeId;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public string StyleName;
        }

        [HarmonyPriority(int.MaxValue)]
        static void BuildModeTogglePostfix(object __Instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                var net = TcpNetworkManager.Instance;
                if (net == null) return;

                _inBuildMode = !_inBuildMode;
                var evtMsg = new MsgBuildModeEvent
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    Tick = MultiplayerSession.Tick,
                    EventType = _inBuildMode ? BuildEventType.ModeEntered : BuildEventType.ModeExited,
                    EntityId = 0,
                    ObjectTypeId = "",
                    Position = Vector3.zero.FromUnity(),
                    Rotation = Quaternion.identity.FromUnity(),
                    Scale = Vector3.one.FromUnity(),
                    StyleName = ""
                };

                if (MultiplayerSession.IsHost)
                    net.SendToAllClients(evtMsg);
                else
                    net.SendToHost(evtMsg);

                PatchLogger.Log($"Build mode toggled: {_inBuildMode}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"BuildModeTogglePostfix error: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static bool IsBuildModeGetterPostfix(object __Instance, ref bool __result)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return true;

            try
            {
                _inBuildMode = __result;
                PatchLogger.LogDebug($"IsBuildMode getter: {_inBuildMode}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"IsBuildModeGetterPostfix error: {ex.Message}");
            }
            return true;
        }

        [HarmonyPriority(int.MaxValue)]
        static void ObjectPlacedPostfix(object __Instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                var go = __Instance as GameObject;
                if (go == null) return;

                var pos = go.transform.position;
                var rot = go.transform.rotation;
                var scale = go.transform.localScale;
                uint seqId = (uint)System.Threading.Interlocked.Increment(ref _buildSequence);

                var evtMsg = new MsgBuildModeEvent
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    Tick = MultiplayerSession.Tick,
                    EventType = BuildEventType.ObjectPlaced,
                    EntityId = seqId,
                    ObjectTypeId = go.name,
                    Position = pos.FromUnity(),
                    Rotation = rot.FromUnity(),
                    Scale = scale.FromUnity(),
                    StyleName = ""
                };

                if (BuildSyncManager.Enabled)
                {
                    BuildSyncManager.ValidateAndApply(evtMsg);
                }

                EntitySyncManager.RegisterSpawn(seqId, go.name, pos, rot, scale, MultiplayerSession.LocalPlayerId);
                PatchLogger.LogDebug($"Object placed: {go.name} at {pos}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"ObjectPlacedPostfix error: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void ObjectDestroyedPostfix(object __Instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                GameObject go = null;
                string objName = "unknown";

                if (__Instance is GameObject instGo)
                {
                    go = instGo;
                    objName = go.name;
                }
                else if (__Instance is Component instComp && instComp.gameObject != null)
                {
                    go = instComp.gameObject;
                    objName = go.name;
                }

                var net = TcpNetworkManager.Instance;
                if (net != null)
                {
                    uint seqId = (uint)System.Threading.Interlocked.Increment(ref _buildSequence);
                    var evtMsg = new MsgBuildModeEvent
                    {
                        PlayerId = MultiplayerSession.LocalPlayerId,
                        Tick = MultiplayerSession.Tick,
                        EventType = BuildEventType.ObjectRemoved,
                        EntityId = seqId,
                        ObjectTypeId = objName,
                        Position = (go != null ? go.transform.position : Vector3.zero).FromUnity(),
                        Rotation = Quaternion.identity.FromUnity(),
                        Scale = Vector3.one.FromUnity(),
                        StyleName = ""
                    };

                    if (MultiplayerSession.IsHost)
                        net.SendToAllClients(evtMsg);
                    else
                        net.SendToHost(evtMsg);

                    EntitySyncManager.RegisterDespawn(seqId, MultiplayerSession.LocalPlayerId);
                }

                PatchLogger.LogDebug($"Object destroyed: {objName}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"ObjectDestroyedPostfix error: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void ObjectTransformChangedPostfix(object __Instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            var now = Time.time;
            if (now - _lastSyncTime < SyncInterval) return;
            _lastSyncTime = now;

            try
            {
                GameObject go = null;
                string objName = "unknown";

                if (__Instance is GameObject instGo)
                {
                    go = instGo;
                    objName = go.name;
                }
                else if (__Instance is Component instComp && instComp.gameObject != null)
                {
                    go = instComp.gameObject;
                    objName = go.name;
                }

                if (go == null) return;

                var pos = go.transform.position;
                var rot = go.transform.rotation;
                var scale = go.transform.localScale;

                var net = TcpNetworkManager.Instance;
                if (net != null)
                {
                    uint seqId = (uint)System.Threading.Interlocked.Increment(ref _buildSequence);
                    var evtMsg = new MsgBuildModeEvent
                    {
                        PlayerId = MultiplayerSession.LocalPlayerId,
                        Tick = MultiplayerSession.Tick,
                        EventType = BuildEventType.ObjectMoved,
                        EntityId = seqId,
                        ObjectTypeId = objName,
                        Position = pos.FromUnity(),
                        Rotation = rot.FromUnity(),
                        Scale = scale.FromUnity(),
                        StyleName = ""
                    };

                    if (MultiplayerSession.IsHost)
                        net.SendToAllClients(evtMsg);
                    else
                        net.SendToHost(evtMsg);
                }

                PatchLogger.LogDebug($"Object transform changed: {objName} pos={pos}, sent network event");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"ObjectTransformChangedPostfix error: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void PlacementPostfix(object __Instance, object[] __args = null)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                var extracted = ExtractObjectData(__Instance, __args);
                uint seqId = (uint)System.Threading.Interlocked.Increment(ref _buildSequence);

                var net = TcpNetworkManager.Instance;
                if (net != null)
                {
                    var evtMsg = new MsgBuildModeEvent
                    {
                        PlayerId = MultiplayerSession.LocalPlayerId,
                        Tick = MultiplayerSession.Tick,
                        EventType = BuildEventType.ObjectPlaced,
                        EntityId = seqId,
                        ObjectTypeId = extracted.TypeId,
                        Position = extracted.Position.FromUnity(),
                        Rotation = extracted.Rotation.FromUnity(),
                        Scale = extracted.Scale.FromUnity(),
                        StyleName = extracted.StyleName
                    };

                    if (MultiplayerSession.IsHost)
                        net.SendToAllClients(evtMsg);
                    else
                        net.SendToHost(evtMsg);

                    EntitySyncManager.RegisterSpawn(seqId, extracted.TypeId, extracted.Position, extracted.Rotation, extracted.Scale, MultiplayerSession.LocalPlayerId);
                }

                PatchLogger.LogDebug($"Placement action: type={extracted.TypeId}, pos={extracted.Position}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"PlacementPostfix error: {ex.Message}");
            }
        }

        public static void SetEnabled(bool value)
        {
            _enabled = value;
            PatchLogger.Log($"Build mode sync {(value ? "enabled" : "disabled")}");
        }
    }
}
