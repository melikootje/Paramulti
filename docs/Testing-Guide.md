# Testing Guide

## Prerequisites

| Requirement | Version | Check |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| Paralives | Latest | Steam library |
| BepInEx | Pre-installed with Paralives | `$PARALIVES_DIR/BepInEx/` exists |

## Quick Install and Test

```bash
./install.sh              # builds, tests, deploys to Paralives
./install.sh Debug        # deploy debug build with PDBs
```

The script:
1. Runs all 56 unit tests
2. Builds the mod in Release mode
3. Backs up any existing plugin DLL
4. Copies `ParalivesMultiplayer.dll` + `ParalivesMultiplayer.Protocol.dll` to `BepInEx/plugins/`

## Manual Build and Test

### Unit Tests

```bash
dotnet test src/ParalivesMultiplayer.Tests/ --verbosity normal
```

Expected: **56 tests pass**, 0 failures.

### Build

```bash
# Debug (with PDBs for debugging)
dotnet build src/ParalivesMultiplayer/ -c Debug

# Release (smaller, optimized)
dotnet build src/ParalivesMultiplayer/ -c Release
```

Output lands in `src/ParalivesMultiplayer/bin/<Config>/netstandard2.0/`.

### Deploy Manually

```bash
cp src/ParalivesMultiplayer/bin/Release/netstandard2.0/ParalivesMultiplayer.dll \
   ~/.local/share/Steam/steamapps/common/Paralives/BepInEx/plugins/

cp src/ParalivesMultiplayer/bin/Release/netstandard2.0/ParalivesMultiplayer.Protocol.dll \
   ~/.local/share/Steam/steamapps/common/Paralives/BepInEx/plugins/
```

## In-Game Testing

### 1. Launch Paralives

Start the game from Steam or via terminal:

```bash
steam run 2506180          # by Steam AppID
# or launch from Steam UI
```

### 2. Verify Mod Loaded

Check the BepInEx console log at:

```
~/.local/share/Steam/steamapps/common/Paralives/BepInEx/Chainloader.log
```

Look for:
- `ParalivesMultiplayer - Loaded` in the plugin list
- No `ERROR` or `Exception` lines from `ParalivesMultiplayer` namespace

### 3. Test Host Session

1. Open Paralives main menu
2. Look for the multiplayer UI overlay (top-left HUD)
3. Start a host session
4. Verify the TCP listener starts on the configured port (default: 27016)

From another terminal, verify the port is open:

```bash
ss -tlnp | grep 27016
# or
netstat -tlnp | grep 27016
```

### 4. Test Client Connection

From a second machine (or same machine with different port):

1. Launch Paralives
2. Use the multiplayer UI to connect to the host IP
3. Verify:
   - Connection established (check log for `Client connected`)
   - Player spawn sync
   - Movement sync between players
   - Build mode events propagate

### 5. Test Build Sync

1. Enter build mode on the host
2. Place an object
3. Verify the client receives the `MsgBuildObjectPlaced` event
4. Remove an object and verify `MsgBuildModeEvent(ObjectRemoved)` syncs

## Debugging

### Enable Verbose Logging

Edit `BepInEx/log.cfg` or set environment variable:

```bash
export BEPINEX_LOG_LEVEL=DEBUG
steam run 2506180
```

### Common Issues

| Symptom | Fix |
|---|---|
| Mod not loading | Check `Chainloader.log` for dependency errors |
| `TypeLoadException` | Unity DLL references mismatch; rebuild against game's assemblies |
| Port bind failure | Another process on port 27016; change config or kill the process |
| Client disconnects immediately | Version mismatch between host and client DLLs |

### Rollback to Previous Build

The installer keeps timestamped backups:

```bash
ls BepInEx/plugins/ParalivesMultiplayer.dll.bak.*
# Restore a specific backup:
cp BepInEx/plugins/ParalivesMultiplayer.dll.bak.20260528074919 \
   BepInEx/plugins/ParalivesMultiplayer.dll
```
