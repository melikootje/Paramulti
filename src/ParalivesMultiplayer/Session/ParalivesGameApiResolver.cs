using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class ParalivesGameApiResolver
    {
        static bool _resolved;
        static readonly List<string> _logMessages = new List<string>();

        public static Type CharacterManagerType { get; private set; }
        public static object CharacterManagerInstance { get; private set; }
        public static MethodInfo LoadCharacterVisualMethod { get; private set; }
        public static MethodInfo GetCharacterByGUIDMethod { get; private set; }
        public static MethodInfo ClearAllCharactersMethod { get; private set; }
        public static MethodInfo RegisterCharacterMethod { get; private set; }
        public static MethodInfo UpdateCharacterPositionRotationAndVisibilityMethod { get; private set; }

        public static Type AssetManagerType { get; private set; }
        public static object AssetManagerInstance { get; private set; }
        public static MethodInfo GetCharacterMethod { get; private set; }
        public static MethodInfo CopyAssetPackageMethod { get; private set; }

        public static Type PlayerManagerType { get; private set; }
        public static object PlayerManagerInstance { get; private set; }
        public static MethodInfo RefreshPlayerInputsMethod { get; private set; }

        public static Type AssetCharacterDataType { get; private set; }
        public static Type AssetCharacterType { get; private set; }
        public static Type CharacterVisualType { get; private set; }
        public static Type HybridPlayerType { get; private set; }
        public static Type HouseholdManagerType { get; private set; }
        public static Type SystemManagerType { get; private set; }

        public static bool IsResolved => _resolved;

        public static void Resolve()
        {
            if (_resolved) return;

            _logMessages.Clear();
            Log("[GameApi] Starting Paralives game API resolution...");

            ScanAssemblies();
            ResolveCharacterManager();
            ResolveAssetManager();
            ResolvePlayerManager();
            ResolveDataTypes();
            ResolveSystemManager();

            _resolved = true;
            Log($"[GameApi] Resolution complete. CharacterManager={CharacterManagerType != null}, AssetManager={AssetManagerType != null}, PlayerManager={PlayerManagerType != null}");
        }

        static void ScanAssemblies()
        {
            Log("[GameApi] Scanning loaded assemblies...");
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                try
                {
                    var name = asm.GetName().Name;
                    if (name != null && (name.Contains("Paralives") || name.Contains("Assembly-CSharp")))
                    {
                        Log($"[GameApi] Found target assembly: {name} ({asm.GetTypes().Length} types)");
                    }
                }
                catch
                {
                }
            }
        }

        static void ResolveCharacterManager()
        {
            Log("[GameApi] Resolving CharacterManager...");
            var candidates = new[] { "CharacterManager", "Paralives.CharacterManager" };

            foreach (var candidate in candidates)
            {
                if (TryFindType(candidate, out var type))
                {
                    CharacterManagerType = type;
                    Log($"[GameApi] Found CharacterManager: {type.FullName}");

                    var staticInstance = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (staticInstance != null)
                    {
                        try
                        {
                            CharacterManagerInstance = staticInstance.GetValue(null);
                            Log($"[GameApi] Got CharacterManager.Instance: {CharacterManagerInstance?.GetType().Name ?? "null"}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[GameApi] Failed to get CharacterManager.Instance: {ex.Message}");
                        }
                    }

                    LoadCharacterVisualMethod = FindMethod(type, "LoadCharacterVisual");
                    GetCharacterByGUIDMethod = FindMethod(type, "GetCharacterByGUID");
                    ClearAllCharactersMethod = FindMethod(type, "ClearAllCharacters");
                    RegisterCharacterMethod = FindMethod(type, "RegisterCharacter");
                    UpdateCharacterPositionRotationAndVisibilityMethod = FindMethod(type, "UpdateCharacterPositionRotationAndVisibility");

                    Log($"[GameApi] LoadCharacterVisual={LoadCharacterVisualMethod != null}");
                    Log($"[GameApi] GetCharacterByGUID={GetCharacterByGUIDMethod != null}");
                    Log($"[GameApi] ClearAllCharacters={ClearAllCharactersMethod != null}");
                    Log($"[GameApi] RegisterCharacter={RegisterCharacterMethod != null}");
                    Log($"[GameApi] UpdateCharacterPositionRotationAndVisibility={UpdateCharacterPositionRotationAndVisibilityMethod != null}");
                    return;
                }
            }

            Log("[GameApi] WARNING: CharacterManager not found");
        }

        static void ResolveAssetManager()
        {
            Log("[GameApi] Resolving AssetManager...");
            var candidates = new[] { "AssetManager", "Paralives.AssetManager" };

            foreach (var candidate in candidates)
            {
                if (TryFindType(candidate, out var type))
                {
                    AssetManagerType = type;
                    Log($"[GameApi] Found AssetManager: {type.FullName}");

                    var staticInstance = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (staticInstance != null)
                    {
                        try
                        {
                            AssetManagerInstance = staticInstance.GetValue(null);
                            Log($"[GameApi] Got AssetManager.Instance: {AssetManagerInstance?.GetType().Name ?? "null"}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[GameApi] Failed to get AssetManager.Instance: {ex.Message}");
                        }
                    }

                    GetCharacterMethod = FindMethod(type, "GetCharacter");
                    CopyAssetPackageMethod = FindMethod(type, "CopyAssetPackage");

                    Log($"[GameApi] GetCharacter={GetCharacterMethod != null}");
                    Log($"[GameApi] CopyAssetPackage={CopyAssetPackageMethod != null}");
                    return;
                }
            }

            Log("[GameApi] WARNING: AssetManager not found");
        }

        static void ResolvePlayerManager()
        {
            Log("[GameApi] Resolving PlayerManager...");
            var candidates = new[] { "PlayerManager", "Paralives.PlayerManager" };

            foreach (var candidate in candidates)
            {
                if (TryFindType(candidate, out var type))
                {
                    PlayerManagerType = type;
                    Log($"[GameApi] Found PlayerManager: {type.FullName}");

                    var staticInstance = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (staticInstance != null)
                    {
                        try
                        {
                            PlayerManagerInstance = staticInstance.GetValue(null);
                            Log($"[GameApi] Got PlayerManager.Instance: {PlayerManagerInstance?.GetType().Name ?? "null"}");
                        }
                        catch (Exception ex)
                        {
                            Log($"[GameApi] Failed to get PlayerManager.Instance: {ex.Message}");
                        }
                    }

                    RefreshPlayerInputsMethod = FindMethod(type, "RefreshPlayerInputs");
                    Log($"[GameApi] RefreshPlayerInputs={RefreshPlayerInputsMethod != null}");
                    return;
                }
            }

            Log("[GameApi] WARNING: PlayerManager not found");
        }

        static void ResolveDataTypes()
        {
            Log("[GameApi] Resolving data types...");

            Type t;
            if (TryFindType("AssetCharacterData", out t)) AssetCharacterDataType = t;
            Log($"[GameApi] AssetCharacterData={AssetCharacterDataType != null}");

            if (TryFindType("AssetCharacter", out t)) AssetCharacterType = t;
            Log($"[GameApi] AssetCharacter={AssetCharacterType != null}");

            if (TryFindType("CharacterVisual", out t)) CharacterVisualType = t;
            Log($"[GameApi] CharacterVisual={CharacterVisualType != null}");

            if (TryFindType("HybridPlayer", out t)) HybridPlayerType = t;
            Log($"[GameApi] HybridPlayer={HybridPlayerType != null}");

            if (TryFindType("HouseholdManager", out t)) HouseholdManagerType = t;
            Log($"[GameApi] HouseholdManager={HouseholdManagerType != null}");
        }

        static void ResolveSystemManager()
        {
            Log("[GameApi] Resolving SystemManager...");
            Type t;
            if (TryFindType("SystemManager", out t)) SystemManagerType = t;
            Log($"[GameApi] SystemManager={SystemManagerType != null}");
        }

        static bool TryFindType(string fullName, out Type type)
        {
            type = null;
            try
            {
                var simpleName = fullName.Contains('.') ? fullName.Split('.').Last() : fullName;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = asm.GetType(fullName);
                        if (type != null) return true;

                        type = asm.GetType(simpleName);
                        if (type != null) return true;
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[GameApi] TryFindType({fullName}) error: {ex.Message}");
            }
            return false;
        }

        static MethodInfo FindMethod(Type type, string name)
        {
            try
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    if (m.Name == name) return m;
                }
            }
            catch (Exception ex)
            {
                Log($"[GameApi] FindMethod({type.Name}.{name}) error: {ex.Message}");
            }
            return null;
        }

        static void Log(object msg)
        {
            Plugin.Log?.LogInfo(msg.ToString() ?? "");
            _logMessages.Add(msg.ToString());
        }

        public static string GetResolutionReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Game API Resolution Report ===");
            foreach (var msg in _logMessages)
                sb.AppendLine(msg);
            return sb.ToString();
        }
    }
}
