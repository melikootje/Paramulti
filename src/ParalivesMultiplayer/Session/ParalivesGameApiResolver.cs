using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class ParalivesGameApiResolver
    {
        static bool _typesScanned;
        static readonly List<string> _logMessages = new List<string>();

        public static Type CharacterManagerType { get; private set; }
        public static object CharacterManagerInstance { get; private set; }
        public static MethodInfo LoadCharacterVisualMethod { get; private set; }
        public static MethodInfo GetCharacterByGUIDMethod { get; private set; }
        public static MethodInfo ClearAllCharactersMethod { get; private set; }
        public static MethodInfo RegisterCharacterMethod { get; private set; }
        public static MethodInfo CreateCharacterByModelGUIDMethod { get; private set; }
        public static MethodInfo GetLoadedCharacterVisualMethod { get; private set; }
        public static MethodInfo SelectCharacterMethod { get; private set; }
        public static MethodInfo DeleteCharacterMethod { get; private set; }

        public static Type AssetManagerType { get; private set; }
        public static object AssetManagerInstance { get; private set; }
        public static MethodInfo GetCharacterMethod { get; private set; }
        public static MethodInfo CopyAssetPackageMethod { get; private set; }

        public static Type PlayerManagerType { get; private set; }
        public static object PlayerManagerInstance { get; private set; }
        public static MethodInfo GetHybridPlayerMethod { get; private set; }

        public static Type HouseholdManagerType { get; private set; }
        public static object HouseholdManagerInstance { get; private set; }
        public static MethodInfo GetCharactersInCurrentHouseholdMethod { get; private set; }

        public static Type AssetCharacterDataType { get; private set; }
        public static Type AssetCharacterType { get; private set; }
        public static Type CharacterVisualType { get; private set; }
        public static Type HybridPlayerType { get; private set; }
        public static Type SystemManagerType { get; private set; }
        public static Type PathfindingManagerType { get; private set; }

        public static bool IsResolved => _typesScanned;

        public static void Resolve()
        {
            if (!_typesScanned)
            {
                _logMessages.Clear();
                Log("[GameApi] Starting Paralives game API resolution...");

                ScanAssemblies();
                ResolveCharacterManager();
                ResolveAssetManager();
                ResolvePlayerManager();
                ResolveHouseholdManager();
                ResolveDataTypes();
                ResolveSystemManager();
                ResolvePathfindingManager();

                _typesScanned = true;
            }

            // Always attempt instance resolution — singletons may not exist at plugin load time
            ResolveInstances();

            Log($"[GameApi] Resolution status: CharacterManager={CharacterManagerInstance != null}, AssetManager={AssetManagerInstance != null}, PlayerManager={PlayerManagerInstance != null}, HouseholdManager={HouseholdManagerInstance != null}");
        }

        static void ResolveInstances()
        {
            if (CharacterManagerType != null && CharacterManagerInstance == null)
            {
                CharacterManagerInstance = GetSingletonInstance(CharacterManagerType);
                Log($"[GameApi] CharacterManager instance: {(CharacterManagerInstance != null ? "found" : "not yet available")}");
            }
            if (AssetManagerType != null && AssetManagerInstance == null)
            {
                AssetManagerInstance = GetSingletonInstance(AssetManagerType);
                Log($"[GameApi] AssetManager instance: {(AssetManagerInstance != null ? "found" : "not yet available")}");
            }
            if (PlayerManagerType != null && PlayerManagerInstance == null)
            {
                PlayerManagerInstance = GetSingletonInstance(PlayerManagerType);
                Log($"[GameApi] PlayerManager instance: {(PlayerManagerInstance != null ? "found" : "not yet available")}");
            }
            if (HouseholdManagerType != null && HouseholdManagerInstance == null)
            {
                HouseholdManagerInstance = GetSingletonInstance(HouseholdManagerType);
                Log($"[GameApi] HouseholdManager instance: {(HouseholdManagerInstance != null ? "found" : "not yet available")}");
            }
        }

        static object GetSingletonInstance(Type type)
        {
            try
            {
                var prop = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (prop != null)
                {
                    var val = prop.GetValue(null);
                    if (val != null) return val;
                }

                var field = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (field != null)
                {
                    var val = field.GetValue(null);
                    if (val != null) return val;
                }

                field = type.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null)
                {
                    var val = field.GetValue(null);
                    if (val != null) return val;
                }
            }
            catch (Exception ex)
            {
                Log($"[GameApi] GetSingletonInstance({type.Name}) error: {ex.Message}");
            }
            return null;
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
            if (TryFindType("CharacterManager", out var type))
            {
                CharacterManagerType = type;
                Log($"[GameApi] Found CharacterManager: {type.FullName}");

                LoadCharacterVisualMethod = FindMethod(type, "LoadCharacterVisual");
                GetCharacterByGUIDMethod = FindMethod(type, "GetCharacterByGUID");
                ClearAllCharactersMethod = FindMethod(type, "ClearAllCharacters");
                RegisterCharacterMethod = FindMethod(type, "RegisterCharacter");
                CreateCharacterByModelGUIDMethod = FindMethod(type, "CreateCharacterByModelGUID");
                GetLoadedCharacterVisualMethod = FindMethod(type, "GetLoadedCharacterVisual");
                SelectCharacterMethod = FindMethod(type, "SelectCharacter");
                DeleteCharacterMethod = FindMethod(type, "DeleteCharacter");

                Log($"[GameApi] LoadCharacterVisual={LoadCharacterVisualMethod != null}");
                Log($"[GameApi] GetCharacterByGUID={GetCharacterByGUIDMethod != null}");
                Log($"[GameApi] ClearAllCharacters={ClearAllCharactersMethod != null}");
                Log($"[GameApi] RegisterCharacter={RegisterCharacterMethod != null}");
                Log($"[GameApi] CreateCharacterByModelGUID={CreateCharacterByModelGUIDMethod != null}");
                Log($"[GameApi] GetLoadedCharacterVisual={GetLoadedCharacterVisualMethod != null}");
                Log($"[GameApi] SelectCharacter={SelectCharacterMethod != null}");
                Log($"[GameApi] DeleteCharacter={DeleteCharacterMethod != null}");
                return;
            }

            Log("[GameApi] WARNING: CharacterManager not found");
        }

        static void ResolveAssetManager()
        {
            Log("[GameApi] Resolving AssetManager...");
            if (TryFindType("AssetManager", out var type))
            {
                AssetManagerType = type;
                Log($"[GameApi] Found AssetManager: {type.FullName}");

                GetCharacterMethod = FindMethod(type, "GetCharacter");
                CopyAssetPackageMethod = FindMethod(type, "CopyAssetPackage");

                Log($"[GameApi] GetCharacter={GetCharacterMethod != null}");
                Log($"[GameApi] CopyAssetPackage={CopyAssetPackageMethod != null}");
                return;
            }

            Log("[GameApi] WARNING: AssetManager not found");
        }

        static void ResolvePlayerManager()
        {
            Log("[GameApi] Resolving PlayerManager...");
            if (TryFindType("PlayerManager", out var type))
            {
                PlayerManagerType = type;
                Log($"[GameApi] Found PlayerManager: {type.FullName}");

                GetHybridPlayerMethod = FindMethod(type, "GetHybridPlayer");
                Log($"[GameApi] GetHybridPlayer={GetHybridPlayerMethod != null}");
                return;
            }

            Log("[GameApi] WARNING: PlayerManager not found");
        }

        static void ResolveHouseholdManager()
        {
            Log("[GameApi] Resolving HouseholdManager...");
            if (TryFindType("HouseholdManager", out var type))
            {
                HouseholdManagerType = type;
                Log($"[GameApi] Found HouseholdManager: {type.FullName}");

                GetCharactersInCurrentHouseholdMethod = FindMethod(type, "GetCharactersInCurrentHousehold");
                Log($"[GameApi] GetCharactersInCurrentHousehold={GetCharactersInCurrentHouseholdMethod != null}");
                return;
            }

            Log("[GameApi] WARNING: HouseholdManager not found");
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
        }

        static void ResolveSystemManager()
        {
            Log("[GameApi] Resolving SystemManager...");
            if (TryFindType("SystemManager", out var t))
            {
                SystemManagerType = t;
                Log($"[GameApi] SystemManager={SystemManagerType != null}");
            }
            else
            {
                Log("[GameApi] WARNING: SystemManager not found");
            }
        }

        static void ResolvePathfindingManager()
        {
            Log("[GameApi] Resolving PathfindingManager...");
            if (TryFindType("PathfindingManager", out var t))
            {
                PathfindingManagerType = t;
                Log($"[GameApi] PathfindingManager={PathfindingManagerType != null}");
            }
            else
            {
                Log("[GameApi] WARNING: PathfindingManager not found");
            }
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

        public static Type ResolveType(string name)
        {
            Type t;
            if (TryFindType(name, out t)) return t;
            return null;
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
