using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ParalivesMultiplayer.Patches
{
    static class PatchLogger
    {
        public static void Log(object msg) => Plugin.Log?.LogInfo($"[Paramulti] {msg}");
        public static void LogDebug(object msg)
        {
            if (Plugin.VerboseLogging)
                Plugin.Log?.LogInfo($"[Paramulti][Debug] {msg}");
        }
        public static void LogError(object msg) => Plugin.Log?.LogError($"[Paramulti] {msg}");
        public static void LogWarning(object msg) => Plugin.Log?.LogWarning($"[Paramulti] {msg}");

        public static bool TryFindMethod(string assemblyName, string typeName, string methodName, out MethodInfo methodInfo)
        {
            methodInfo = null;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var a in asm)
                {
                    if (!string.IsNullOrEmpty(assemblyName) && !a.GetName().Name.StartsWith(assemblyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var type = a.GetType(typeName);
                    if (type == null) continue;

                    methodInfo = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                    if (methodInfo != null)
                    {
                        LogDebug($"Found target: {type.FullName}.{methodName}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Assembly scan error: {ex.Message}");
            }
            return false;
        }

        public static bool TryFindType(string assemblyName, string typeName, out Type type)
        {
            type = null;
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.IsNullOrEmpty(assemblyName) && !a.GetName().Name.StartsWith(assemblyName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    type = a.GetType(typeName);
                    if (type != null)
                    {
                        LogDebug($"Found type: {type.FullName}");
                        return true;
                    }

                    // Fallback: scan all types for simple name match
                    foreach (var t in a.GetTypes())
                    {
                        if (t.Name == typeName)
                        {
                            type = t;
                            LogDebug($"Found type: {type.FullName}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Type scan error: {ex.Message}");
            }
            return false;
        }

        public static void SafePatch(Harmony harmony, MethodInfo target, HarmonyMethod postfix, string label)
        {
            try
            {
                harmony.Patch(target, prefix: null, postfix: postfix);
                Log($"Applied POSTFIX: {label} -> {target.DeclaringType?.Name}.{target.Name}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to patch {label}: {ex.Message}");
            }
        }

        public static void SafePatchPrefix(Harmony harmony, MethodInfo target, HarmonyMethod prefix, string label)
        {
            try
            {
                harmony.Patch(target, prefix: prefix);
                Log($"Applied PREFIX: {label} -> {target.DeclaringType?.Name}.{target.Name}");
            }
            catch (Exception ex)
            {
                LogError($"Failed to patch {label}: {ex.Message}");
            }
        }
    }
}
