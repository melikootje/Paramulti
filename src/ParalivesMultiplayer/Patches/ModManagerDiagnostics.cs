using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class ModManagerDiagnostics
    {
        const string LOG = "[Paramulti/ModManagerDiag]";
        const string DIAGFILE = "BepInEx/ModManagerDiag.log";

        public static void Apply(Harmony harmony)
        {
            try
            {
                File.WriteAllText(DIAGFILE, $"[{DateTime.Now:O}] ModManagerDiagnostics.Apply\n");

                Type modManagerType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("ModManager", throwOnError: false);
                        if (t != null) { modManagerType = t; break; }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(DIAGFILE, $"  asm {asm.GetName().Name} GetType failed: {ex.GetType().Name}: {ex.Message}\n");
                    }
                }

                if (modManagerType == null)
                {
                    File.AppendAllText(DIAGFILE, "ModManager type not found\n");
                    return;
                }

                // SCAN FIRST: walk all loaded assemblies and find which ones produce
                // ReflectionTypeLoadException. We do this once at install time and dump
                // the offending types to the diag file. This is independent of the
                // game's ModManager call — we get the answer up front.
                File.AppendAllText(DIAGFILE, $"Scanning {AppDomain.CurrentDomain.GetAssemblies().Length} assemblies for type-load failures...\n");
                int assembliesWithFailures = 0;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException rtle)
                    {
                        assembliesWithFailures++;
                        File.AppendAllText(DIAGFILE, $"\n=== ASSEMBLY WITH FAILURES: {asm.GetName().Name} ({asm.GetName().Version}) ===\n");
                        File.AppendAllText(DIAGFILE, $"  loaded types = {rtle.Types?.Length ?? -1}\n");
                        int n = 0;
                        foreach (var le in rtle.LoaderExceptions)
                        {
                            n++;
                            if (le == null) continue;
                            File.AppendAllText(DIAGFILE, $"  LoaderEx[{n}] {le.GetType().Name}: {le.Message}\n");
                            try
                            {
                                var tn = ExtractTypeName(le.Message);
                                if (tn != null) File.AppendAllText(DIAGFILE, $"      -> suspect type: {tn}\n");
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        File.AppendAllText(DIAGFILE, $"  asm {asm.GetName().Name} GetTypes() threw non-RTLE: {ex.GetType().Name}: {ex.Message}\n");
                    }
                }
                File.AppendAllText(DIAGFILE, $"\n=== Summary: {assembliesWithFailures} assemblies produced ReflectionTypeLoadException ===\n");

                // Now also install a true PREFIX (bool return) that fires before the
                // game's call. We don't try to catch the exception there (6ix does),
                // we just confirm the call site is reached.
                var refresh = modManagerType.GetMethod("RefreshCurrentlyLoadedMods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (refresh != null)
                {
                    var prefix = typeof(ModManagerDiagnostics).GetMethod(nameof(Refresh_Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(refresh, prefix: new HarmonyMethod(prefix));
                    File.AppendAllText(DIAGFILE, $"Patched RefreshCurrentlyLoadedMods with true prefix\n");
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(DIAGFILE, $"Apply failed: {ex}\n");
            }
        }

        static bool Refresh_Prefix(object[] __args)
        {
            // Throttle logging: only log every ~2s.
            DateTime now = DateTime.Now;
            if ((now - _lastLog).TotalSeconds >= 2.0)
            {
                _lastLog = now;
                File.AppendAllText(DIAGFILE, $"[{now:O}] Refresh_Prefix called (true prefix) — return TRUE; letting the 6ix finalizer handle any exception\n");
            }
            // LET THE ORIGINAL METHOD RUN. The 6ix StopReimporting finalizer catches
            // any ReflectionTypeLoadException; the 6ix SteamOfflineFix postfix sets
            // CompletedInitialLaunchDownloads=true on SteamworksService.Update (which
            // needs the main thread free, which only happens if we don't busy-loop).
            // Returning false here short-circuits whatever the game was waiting for
            // and leaves the boot scene hanging.
            return true;
        }

        static DateTime _lastLog = DateTime.MinValue;

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
