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

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (name == null) continue;
                    if (!name.Contains("Paralives") && !name.Contains("Assembly-CSharp")) continue;

                    foreach (var candidate in candidateNames)
                    {
                        buildMgrType = asm.GetType(candidate);
                        if (buildMgrType != null) break;
                    }
                    if (buildMgrType != null) break;
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

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (name == null) continue;
                    if (!name.Contains("Paralives") && !name.Contains("Assembly-CSharp")) continue;

                    foreach (var candidate in candidates)
                    {
                        buildObjType = asm.GetType(candidate);
                        if (buildObjType != null) break;
                    }
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

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name;
                    if (name == null) continue;
                    if (!name.Contains("Paralives") && !name.Contains("Assembly-CSharp")) continue;

                    foreach (var candidate in candidates)
                    {
                        placementType = asm.GetType(candidate);
                        if (placementType != null) break;
                    }
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
        static void BuildActionPostfix(object __Instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                var net = TcpNetworkManager.Instance;
                if (net == null) return;

                uint seqId = (uint)System.Threading.Interlocked.Increment(ref _buildSequence);
                var evtMsg = new MsgBuildModeEvent
                {
                    PlayerId = 0,
                    Tick = MultiplayerSession.Tick,
                    EventType = BuildEventType.ObjectPlaced,
                    EntityId = seqId,
                    ObjectTypeId = "unknown",
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one,
                    StyleName = ""
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

                PatchLogger.LogDebug($"Build action observed, sent event seq={seqId}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"BuildActionPostfix error: {ex.Message}");
            }
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
                    PlayerId = 0,
                    Tick = MultiplayerSession.Tick,
                    EventType = _inBuildMode ? BuildEventType.ModeEntered : BuildEventType.ModeExited,
                    EntityId = 0,
                    ObjectTypeId = "",
                    Position = Vector3.zero,
                    Rotation = Quaternion.identity,
                    Scale = Vector3.one,
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
                    PlayerId = 0,
                    Tick = MultiplayerSession.Tick,
                    EventType = BuildEventType.ObjectPlaced,
                    EntityId = seqId,
                    ObjectTypeId = go.name,
                    Position = pos,
                    Rotation = rot,
                    Scale = scale,
                    StyleName = ""
                };

                if (BuildSyncManager.Enabled)
                {
                    BuildSyncManager.ValidateAndApply(evtMsg);
                }

                EntitySyncManager.RegisterSpawn(seqId, go.name, pos, rot, scale, 0);
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
                PatchLogger.LogDebug("Object destroyed observed");
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
                var go = __Instance as GameObject;
                if (go == null) return;

                var pos = go.transform.position;
                var rot = go.transform.rotation;
                var scale = go.transform.localScale;

                PatchLogger.LogDebug($"Object transform changed: {go.name} pos={pos}");
            }
            catch (Exception ex)
            {
                PatchLogger.LogError($"ObjectTransformChangedPostfix error: {ex.Message}");
            }
        }

        [HarmonyPriority(int.MaxValue)]
        static void PlacementPostfix(object __Instance)
        {
            if (!MultiplayerSession.IsActive || !_enabled) return;

            try
            {
                PatchLogger.LogDebug("Placement system action observed");
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
