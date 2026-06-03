using System;
using System.Reflection;
using HarmonyLib;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Patches
{
    static class GameLoopPatches
    {
        public static void Apply(Harmony harmony)
        {
            // DISABLED 2026-06-03: SystemManager.LateUpdate postfix was the most likely
            // cause of the loading-screen hang when our mod is loaded without the 6ix
            // StopReimporting guard plugin installed. Plugin.Update() (a regular
            // MonoBehaviour Update on the BepInEx GameObject) drives OnGameUpdate on
            // its own; the SystemManager hook is redundant.
            //
            // Keep the rest of the patch surface (SceneManager, PlayerState) but
            // never touch the game's core SystemManager.LateUpdate. If we ever need
            // it back, gate it behind a config flag so it defaults off.
        }
    }
}
