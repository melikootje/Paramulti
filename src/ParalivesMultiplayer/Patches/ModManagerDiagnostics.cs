using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class ModManagerDiagnostics
    {
        const string LOG = "[Paramulti/ModManagerDiag]";

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type modManagerType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("ModManager", throwOnError: false);
                        if (t != null) { modManagerType = t; break; }
                    }
                    catch { }
                }

                if (modManagerType == null)
                {
                    Plugin.Log?.LogInfo($"{LOG} ModManager type not found, skipping diagnostic patches.");
                    return;
                }

                var refresh = modManagerType.GetMethod("RefreshCurrentlyLoadedMods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (refresh != null)
                {
                    var finalizer = typeof(ModManagerDiagnostics).GetMethod(nameof(Refresh_Finalizer), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(refresh, finalizer: new HarmonyMethod(finalizer));
                    Plugin.Log?.LogInfo($"{LOG} finalizer installed on ModManager.RefreshCurrentlyLoadedMods");
                }

                var loadAll = modManagerType.GetMethod("LoadAllMods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (loadAll != null)
                {
                    var finalizer = typeof(ModManagerDiagnostics).GetMethod(nameof(LoadAll_Finalizer), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(loadAll, finalizer: new HarmonyMethod(finalizer));
                    Plugin.Log?.LogInfo($"{LOG} finalizer installed on ModManager.LoadAllMods");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"{LOG} failed: {ex.Message}");
            }
        }

        static Exception Refresh_Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            LogInnerExceptions("RefreshCurrentlyLoadedMods", __exception);
            return null;
        }

        static Exception LoadAll_Finalizer(Exception __exception)
        {
            if (__exception == null) return null;
            LogInnerExceptions("LoadAllMods", __exception);
            return null;
        }

        static void LogInnerExceptions(string method, Exception ex)
        {
            Plugin.Log?.LogError($"{LOG} {method} threw: {ex.GetType().Name}: {ex.Message}");
            if (ex is ReflectionTypeLoadException rtle)
            {
                int n = 0;
                foreach (var le in rtle.LoaderExceptions)
                {
                    n++;
                    if (le == null) continue;
                    Plugin.Log?.LogError($"{LOG}   [{n}] {le.GetType().Name}: {le.Message}");
                    // Try to identify the assembly that owns the failing type
                    try
                    {
                        var typeName = ExtractTypeName(le.Message);
                        if (typeName != null)
                        {
                            Plugin.Log?.LogError($"{LOG}     -> suspect type: {typeName}");
                        }
                    }
                    catch { }
                }
                Plugin.Log?.LogError($"{LOG}   loaded types count = {rtle.Types?.Length ?? -1}");
            }
        }

        static string ExtractTypeName(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            int idx = message.IndexOf("type '", StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + 6;
            int end = message.IndexOf('\'', start);
            if (end < 0) return null;
            return message.Substring(start, end - start);
        }
    }
}
