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
        static readonly HashSet<string> _singletonScannedTypes = new HashSet<string>();

        static bool _loggedCharMgrMissing;
        static bool _loggedAssetMgrMissing;
        static bool _loggedPlayerMgrMissing;
        static bool _loggedHouseholdMgrMissing;
        static bool _resolutionReported;
        static float _lastResolutionLogTime = -999f;

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

            bool allResolved = CharacterManagerInstance != null && AssetManagerInstance != null &&
                               PlayerManagerInstance != null && HouseholdManagerInstance != null;

            if (allResolved && !_resolutionReported)
            {
                _resolutionReported = true;
                Log("[GameApi] All manager instances resolved successfully.");
            }
            else if (!allResolved && !_resolutionReported)
            {
                // Throttle unresolved summary to once every 5 seconds
                float now = UnityEngine.Time.time;
                if (now - _lastResolutionLogTime >= 5f)
                {
                    _lastResolutionLogTime = now;
                    Log($"[GameApi] Resolution status: CharacterManager={CharacterManagerInstance != null}, AssetManager={AssetManagerInstance != null}, PlayerManager={PlayerManagerInstance != null}, HouseholdManager={HouseholdManagerInstance != null}");
                }
            }
        }

        public static void LogHybridPlayerFields(object hybridPlayer)
        {
            if (hybridPlayer == null) return;
            var type = hybridPlayer.GetType();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[GameApi] HybridPlayer fields ({type.FullName}):");
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                var val = f.GetValue(hybridPlayer);
                sb.AppendLine($"  {f.FieldType.Name} {f.Name} = {val}");
            }
            sb.AppendLine($"[GameApi] HybridPlayer properties:");
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
            {
                try
                {
                    var val = p.GetValue(hybridPlayer);
                    sb.AppendLine($"  {p.PropertyType.Name} {p.Name} = {val}");
                }
                catch { sb.AppendLine($"  {p.PropertyType.Name} {p.Name} = <error>"); }
            }
            Log(sb.ToString());
        }

        static void ResolveInstances()
        {
            if (CharacterManagerType != null && CharacterManagerInstance == null)
            {
                CharacterManagerInstance = GetSingletonInstance(CharacterManagerType);
                if (CharacterManagerInstance != null)
                    Log("[GameApi] CharacterManager instance: found");
                else if (!_loggedCharMgrMissing)
                {
                    _loggedCharMgrMissing = true;
                    Log("[GameApi] CharacterManager instance: not yet available (will retry)");
                }
            }
            if (AssetManagerType != null && AssetManagerInstance == null)
            {
                AssetManagerInstance = GetSingletonInstance(AssetManagerType);
                if (AssetManagerInstance != null)
                    Log("[GameApi] AssetManager instance: found");
                else if (!_loggedAssetMgrMissing)
                {
                    _loggedAssetMgrMissing = true;
                    Log("[GameApi] AssetManager instance: not yet available (will retry)");
                }
            }
            if (PlayerManagerType != null && PlayerManagerInstance == null)
            {
                PlayerManagerInstance = GetSingletonInstance(PlayerManagerType);
                if (PlayerManagerInstance != null)
                    Log("[GameApi] PlayerManager instance: found");
                else if (!_loggedPlayerMgrMissing)
                {
                    _loggedPlayerMgrMissing = true;
                    Log("[GameApi] PlayerManager instance: not yet available (will retry)");
                }
            }
            if (HouseholdManagerType != null && HouseholdManagerInstance == null)
            {
                HouseholdManagerInstance = GetSingletonInstance(HouseholdManagerType);
                if (HouseholdManagerInstance != null)
                    Log("[GameApi] HouseholdManager instance: found");
                else if (!_loggedHouseholdMgrMissing)
                {
                    _loggedHouseholdMgrMissing = true;
                    Log("[GameApi] HouseholdManager instance: not yet available (will retry)");
                }
            }
        }

        static object GetSingletonInstance(Type type)
        {
            try
            {
                // Case-insensitive property search
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (prop.Name.Equals("Instance", StringComparison.OrdinalIgnoreCase) && prop.GetGetMethod(true) != null)
                    {
                        var val = prop.GetValue(null);
                        if (val != null) return val;
                    }
                }

                // Case-insensitive field search for common singleton patterns
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (field.Name.Equals("Instance", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals("_instance", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals("m_Instance", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals("s_Instance", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals("instance", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals($"_{type.Name}", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals($"m_{type.Name}", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals($"s_{type.Name}", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.Equals($"_{char.ToLower(type.Name[0])}{type.Name.Substring(1)}", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = field.GetValue(null);
                        if (val != null) return val;
                    }
                }

                // Auto-generated backing field
                var backing = type.GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                if (backing != null)
                {
                    var val = backing.GetValue(null);
                    if (val != null) return val;
                }

                // Static method fallback: GetInstance() or Get()
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                {
                    if (method.Name.Equals("GetInstance", StringComparison.OrdinalIgnoreCase) ||
                        method.Name.Equals("Get", StringComparison.OrdinalIgnoreCase) ||
                        method.Name.Equals($"Get{type.Name}", StringComparison.OrdinalIgnoreCase))
                    {
                        if (method.GetParameters().Length == 0 && method.ReturnType != typeof(void))
                        {
                            var val = method.Invoke(null, null);
                            if (val != null) return val;
                        }
                    }
                }

                // One-time diagnostic: log all static fields/properties found (even if null)
                if (_singletonScannedTypes.Add(type.FullName))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[GameApi] Singleton scan for {type.FullName}:");
                    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        sb.AppendLine($"  Prop: {prop.Name} (hasGet={prop.GetGetMethod(true) != null})");
                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        sb.AppendLine($"  Field: {field.Name}");
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
                        if (method.GetParameters().Length == 0 && method.ReturnType != typeof(void))
                            sb.AppendLine($"  Method: {method.Name} -> {method.ReturnType.Name}");
                    Log(sb.ToString());
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

                        // Fallback: scan all exported types by simple name (handles namespaced types)
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.Name == simpleName)
                            {
                                type = t;
                                return true;
                            }
                        }
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

        public static Type FindTypeBySimpleName(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var asmName = asm.GetName().Name;
                    if (asmName == null) continue;
                    if (!asmName.Contains("Paralives") && !asmName.Contains("Assembly-CSharp")) continue;

                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name == simpleName) return t;
                    }
                }
                catch { }
            }
            return null;
        }

        public static Type FindTypeByPartialName(string partialName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var asmName = asm.GetName().Name;
                    if (asmName == null) continue;
                    if (!asmName.Contains("Paralives") && !asmName.Contains("Assembly-CSharp")) continue;

                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name.IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0) return t;
                    }
                }
                catch { }
            }
            return null;
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
