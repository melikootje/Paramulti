# Paralives Multiplayer Mod

A clean-room multiplayer mod for [Paralives](https://paralives.com/) built on BepInEx + Harmony. Implements host-authoritative TCP networking with tick-based synchronization, build-mode collaboration, and live player state sync.

## Architecture

```
src/ParalivesMultiplayer/
├── Plugin.cs                  # BepInEx entry point, config, patch wiring
├── Networking/
│   ├── TcpNetworkManager.cs   # TCP host/client, sender/receiver threads
│   ├── MessageRegistry.cs     # Type-safe message routing by string code
│   ├── MainThreadQueue.cs     # Thread-safe Unity main-thread dispatch
│   ├── ChatManager.cs         # In-game chat relay
│   ├── PacketStats.cs         # Bandwidth/ping/error counters
│   ├── BinarySerializationEx.cs # Vector3/Quaternion extension methods
│   └── Messages/              # 17 message types (Connect, Disconnect,
│                               #     PlayerJoin/Leave, SyncState, UpdateState,
│                               #     Chat, CursorPing, BuildObjectPlaced,
│                               #     EntitySpawn/Despawn, RequestFullState,
│                               #     FullStateSnapshot, ReadyCheck,
│                               #     InputCommand, BuildModeEvent, Heartbeat)
├── Session/
│   ├── MultiplayerSession.cs  # Session lifecycle, tick counter, player list
│   ├── LobbyManager.cs        # Player ready-check system
│   ├── BuildSyncManager.cs    # Host-authoritative build event validation
│   └── EntitySyncManager.cs   # Entity spawn/despawn tracking + snapshots
├── Patches/
│   ├── SceneManagementPatches.cs  # SceneManager.LoadScene* postfix hooks
│   ├── GameLifecyclePatches.cs    # ReturnToMenu, RestartLevel, QuitGame
│   ├── PlayerStatePatches.cs      # PlayerController FixedUpdate/Update sync
│   ├── BuildModePatches.cs        # BuildManager placement/destruction hooks
│   └── PatchLogger.cs             # Safe patch helpers, assembly scanning
├── Input/
│   ├── InputRouter.cs       # Remote input tracking, decay, button state
│   └── CommandHandler.cs    # F5=Host, F6=Client, F7=Disconnect hotkeys
├── Performance/
│   ├── BandwidthLimiter.cs  # Token-bucket rate limiter
│   ├── DeltaCompressor.cs   # Transform delta threshold filtering
│   └── DesyncDetector.cs    # Heartbeat monitoring, tick drift detection
├── UI/
│   └── MultiplayerHUD.cs    # Debug overlay: ping, packets, chat, session info
└── Stubs/
    ├── BepInEx.Attributes.cs  # Compile-time stubs for BepInEx attributes
    └── BepInEx.Stubs.cs       # BaseUnityPlugin, ConfigEntry, ManualLogSource
```

## Design Decisions

| Decision | Rationale |
|----------|-----------|
| **TCP over UDP** | Local-only multiplayer; reliability > raw speed |
| **Message Registry Pattern** | Type-safe routing via `Dictionary<string, MessageBase>` |
| **MainThreadQueue** | Thread-safe dispatch from network threads to Unity main thread |
| **Postfix patches preferred** | Observe game state after it changes; Prefix only for blocking |
| **Host-authoritative** | Host validates all state changes; clients send intent only |
| **Tick-based sync** | Consistent synchronization boundaries across all subsystems |
| **Heavy logging** | Every subsystem logs at Info/Debug/Warning/Error levels |

## Quick Start

### Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for Paralives
2. Build the project and copy `ParalivesMultiplayer.dll` to `BepInEx/plugins/`
3. Launch Paralives

### Controls

| Key | Action |
|-----|--------|
| `F5` | Start as HOST (listens on configured port) |
| `F6` | Connect as CLIENT to host address |
| `F7` | Disconnect from current session |

### Configuration

Edit `BepInEx/config/com.paralives.multiplayer.cfg`:

```ini
[Network]
# TCP port for host to listen on
ListenPort=7890
# Default address for clients to connect to
ConnectAddress=127.0.0.1

[Harmony]
# Enable Harmony patches for game integration
EnablePatches=True

[BuildSync]
# Log build events without applying them (safe default)
DryRunMode=False
# Apply remote build events to local entity state
RealApplyMode=False

[Debug]
# Enable verbose network and patch logging
VerboseLogging=False
```


## Reference Codebases

Clean-room analysis only -- no code was copied. Reference mods studied for architectural patterns:

| Repository | Purpose |
|------------|---------|
| `reference-mods/CupHeads/` | Main-thread queue pattern, message registry, Harmony patch structure |
| `reference-mods/FeatMultiplayer/` | TCP networking, session management, state sync |
| `reference-mods/RavenM/` | Build-mode synchronization, entity tracking |
| `reference-mods/LiteNetLib/` | Network protocol design, packet serialization |

See `docs/Architectural-Analysis-Report.md` for detailed findings.

## Documentation

- [`docs/Implementation-Plan.md`](docs/Implementation-Plan.md) -- Phased milestones (Phases 1-11)
- [`docs/Architectural-Analysis-Report.md`](docs/Architectural-Analysis-Report.md) -- Reference codebase analysis
- [`docs/Key-Files.md`](docs/Key-Files.md) -- File-by-file mapping and design notes

## Building

```bash
# Requires .NET SDK 6.0+ and Unity player assemblies referenced at runtime
dotnet build src/ParalivesMultiplayer/ParalivesMultiplayer.csproj -c Release
# Output: src/ParalivesMultiplayer/bin/Release/ParalivesMultiplayer.dll
```

The project uses BepInEx stubs for compile-time type checking. At runtime, the real BepInEx assemblies are loaded from the game's BepInEx installation.

## License

MIT
