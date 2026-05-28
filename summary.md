# Session Summary: ParalivesMultiplayer Input Fix

## Goal
Fix F5/F6/F7 key bindings so they reliably trigger host/client/disconnect actions in the ParalivesMultiplayer BepInEx plugin for Paralives.

## Root Causes Found and Fixed

### 1. HarmonyX NuGet package pulling in MonoMod.Backports (CRASH)
- **Problem**: The `HarmonyX` NuGet package referenced `MonoMod.Backports` which does not exist at runtime, causing a `FileNotFoundException` that crashed Awake() before any logging could run.
- **Fix**: Removed `<PackageReference Include="HarmonyX" />` from `.csproj`. Added direct reference to the game's bundled `0Harmony.dll` from BepInEx core instead.
- **Files Changed**: `src/ParalivesMultiplayer/ParalivesMultiplayer.csproj`

### 2. Unity.InputSystem.dll missing at runtime (CRASH)
- **Problem**: The project referenced `Unity.InputSystem.dll` directly, but the assembly was not loadable at runtime, causing another `MonoMod.Backports` `FileNotFoundException`.
- **Fix**: Removed the direct reference from `.csproj`. Rewrote `CommandHandler.cs` to use reflection to detect and access Unity Input System types at runtime. Falls back to legacy `UnityEngine.Input.GetKeyDown` if the new system is unavailable or fails.
- **Files Changed**: `src/ParalivesMultiplayer/Input/CommandHandler.cs`, `src/ParalivesMultiplayer/ParalivesMultiplayer.csproj`

### 3. Wrong Log API usage
- **Problem**: Code called `Plugin.LogWarning` which does not exist on the Plugin class.
- **Fix**: Changed all calls to `Plugin.Log.LogWarning` (the correct BepInEx ManualLogSource property).
- **Files Changed**: `src/ParalivesMultiplayer/Input/CommandHandler.cs`

### 4. Missing using directive and static method call
- **Problem**: `FirstOrDefault()` extension used without `using System.Linq;`. Also `SessionWatchdog.Dispose()` was called as a static method incorrectly.
- **Fix**: Added `using System.Linq;` to CommandHandler.cs. Fixed the Dispose call in Plugin.cs OnDestroy.

## What Was Built

### CommandHandler.cs (complete rewrite)
- Uses reflection to safely access Unity Input System without compile-time dependencies
- Tries new Input System first (`Keyboard.current.f5Key.wasPressedThisFrame`), falls back to legacy `Input.GetKeyDown(KeyCode.F5)`
- Three key bindings: F5 = Host, F6 = Client, F7 = Disconnect
- All logging wrapped in try-catch to prevent crashes from null Log references

### Plugin.cs (major refactor)
- Added `DontDestroyOnLoad(gameObject)` and `gameObject.hideFlags` for persistence across scene loads
- 13-step initialization with detailed logging: MessageRegistry, DesyncDetector, InputRouter, BuildSyncManager, ReconnectionManager, ErrorRecoveryManager, ConnectionQualityMonitor, SessionWatchdog, MessageAuthenticator, RateLimiter, EntitySync event wiring, Harmony patches, CommandHandler
- Update loop calls: MainThreadQueue.Drain, ProcessIncomingMessages, ProcessInput, HUD update, heartbeat/ping timers
- OnDestroy cleanup for all subsystems

### Project file cleanup
- Removed `Unity.InputSystem.dll` reference
- Removed `HarmonyX` NuGet package
- Added `0Harmony.dll` direct reference from bundled BepInEx
- Added `UnityEngine.InputLegacyModule.dll` reference for fallback input
- Deleted unused stub files: `Stubs/BepInEx.Attributes.cs`, `Stubs/BepInEx.Stubs.cs`

## Current Status

### Working
- Plugin builds and deploys without errors
- All 13 initialization steps complete successfully
- Harmony patches applied to SceneManager.LoadScene
- Input detection initialized (both new and legacy systems)
- No more MonoMod.Backports crash

### Still Broken
- The `BaseUnityPlugin` GameObject is destroyed immediately after Awake() completes, even with `DontDestroyOnLoad`. OnDestroy fires right after chainloader startup finishes.
- This means the Update() loop never runs, so input processing and network message handling are dead code.
- Some Harmony patches silently skip because target game types (GameManager, BuildManager, Player.Update) could not be found in Assembly-CSharp.dll

### Investigation In Progress
- Looking at BepInEx GitHub issue #420 about BaseUnityPlugin destruction after Awake
- Plan: abandon MonoBehaviour.Update reliance entirely; migrate to static state management and patch into the game's own update loop via Harmony
- Need to inspect Assembly-CSharp.dll for a persistent game type with an Update/FixedUpdate method to hook into

## Key Files
- `src/ParalivesMultiplayer/Plugin.cs` - Main plugin lifecycle (destroyed after Awake)
- `src/ParalivesMultiplayer/Input/CommandHandler.cs` - Reflection-based input handler
- `src/ParalivesMultiplayer/ParalivesMultiplayer.csproj` - Fixed references
- `references/Assembly-CSharp.dll` - Game assembly to inspect for update loop targets

## Logs
- BepInEx log: `~/.local/share/Steam/steamapps/common/Paralives/BepInEx/LogOutput.log`
- Unity log: `~/.local/share/Steam/steamapps/common/Paralives/output_log.txt`
