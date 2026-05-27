# Architectural Analysis Report

## Overview
This report analyzes four reference codebases for a clean-room BepInEx multiplayer prototype for Paralives. The analysis extracts architectural patterns without copying source code.

## Reference Codebases Analyzed

### 1. CupHeads/CupheadOnline
**License:** Not explicitly checked (assume proprietary)
**Networking:** Custom UDP with reliable/unreliable delivery
**Architecture:** Static session state, tick-based sync, main-thread queue dispatch

#### Key Patterns Extracted:
- **BepInEx Entry Point:** `Plugin.cs` initializes networking manager, config entries, and Harmony patches in `Awake()`
- **Session Management:** Static `MultiplayerSession` singleton with `IsActive`, `IsHost`, `IsClient` state flags
- **Networking Layer:**
  - UDP-based with custom binary protocol
  - Reliable delivery via sequence numbers + ACK + retransmit (200ms interval, max 25 retries)
  - Unreliable delivery for high-frequency packets (player state, input frames)
  - Control messages: CONNECT_REQ/ACK, DISCONNECT, KEEPALIVE, ACK
  - Connection handshake with shared key validation
- **Thread Safety:**
  - `MainThreadQueue` pattern: Queue<Action> + lock for thread-safe dispatch from network thread to Unity main thread
  - Network receive loop runs on background thread, enqueues raw packets
  - Main thread drains queue in `Poll()` called every frame from `Plugin.Update()`
  - Cap of 128 actions per frame to prevent stalls
- **Packet Dispatcher:** Switch-based routing by packet type enum to handler methods
- **Patching Strategy:** Postfix Harmony patches for game lifecycle control (scene loading, player spawning, menu returns)

#### File Structure:
```
CupheadOnline/
├── Plugin.cs              # BepInEx entry, config, Harmony patches
├── MultiplayerSession.cs  # Static session state manager
├── Net/
│   ├── MainThreadQueue.cs # Thread-safe dispatch queue
│   ├── NetManager.cs      # UDP networking manager
│   ├── PacketDispatcher.cs# Packet routing
│   ├── Packets.cs         # Packet struct definitions
│   └── SteamNetManager.cs # Alternative Steam networking
├── Patches/               # Harmony patches for game integration
├── Sync/                  # Synchronization managers
└── UI/                    # HUD and menu components
```

### 2. plan-b-terraform-mods/FeatMultiplayer
**License:** Apache 2.0 (permissive, allows clean-room reuse of patterns)
**Networking:** TCP-based with message registry pattern
**Architecture:** Modular plugin architecture, deferred message processing

#### Key Patterns Extracted:
- **BepInEx Entry Point:** `Plugin.cs` initializes config, logging, GUI, and message dispatcher in `Awake()`
- **Networking Layer:**
  - TCP-based with length-prefixed messages
  - Message code registry pattern: Dictionary<string, MessageBase> for type routing
  - Sender/Receiver loops per client session on background threads
  - ConcurrentQueue for thread-safe message passing between threads
  - AutoResetEvent signal for efficient sender loop wake-up
- **Message Protocol:**
  - Header: [totalLength:4][codeLen:1][codeBytes:N][payload:M]
  - UTF-8 encoded message codes (e.g., "MsgSyncAllMain")
  - Abstract `MessageBase` class with `Encode()`, `TryDecode()`, `MessageCode()` methods
  - Extension methods for Vector3, Quaternion, CTransform serialization
- **Session Management:**
  - `ClientSession` class per connection with unique ID
  - Host accepts connections via TcpListener on background thread
  - Client connects to host address/port
  - Deferred message queue during initial sync phase
- **Modular Architecture:** Separate plugin files for networking, sync, session, actions, simulation

#### File Structure:
```
FeatMultiplayer/
├── Plugin.cs              # Main entry, config, logging
├── Plugin_Networking.cs   # TCP networking layer
├── Plugin_Messaging.cs    # Message registry and dispatch
├── Plugin_Session.cs      # Session management
├── Plugin_Sync.cs         # State synchronization
├── Plugin_*.cs            # Action handlers, simulation, etc.
├── MessageTypes/          # Message definitions
│   ├── MessageBase.cs     # Abstract message class
│   └── Message*.cs        # Concrete message types
└── ...
```

### 3. RavenM
**License:** MIT (permissive)
**Networking:** SteamNetworkingSockets
**Architecture:** Host authority, AI coroutine disabling on clients

#### Key Patterns Extracted:
- **BepInEx Entry Point:** `Plugin.cs` with mod path restriction patch, Steamworks init tracking
- **Session Management:** Lobby system via Steam Matchmaking API
- **Patching Strategy:**
  - Patches `GameManager.ReturnToMenu` to close connections on menu return
  - Patches `GameManager.RestartLevel` to block client restarts (host authority)
  - Patches `Actor.IsReadyToSpawn` for spawn control
  - Disables AI coroutines on non-host clients to prevent desync
- **Network Protocol:** Custom binary with VarInt encoding

### 4. LiteNetLib
**License:** MIT (permissive)
**Type:** Networking library (not a mod)
**Features:** Multiple delivery modes, reflection-based serialization

#### Key Patterns Extracted:
- Delivery modes: `Unreliable`, `ReliableOrdered`, `Sequenced`, `UnreliableOrdered`
- FNV-1 64-bit type hashing for message routing
- Reflection-based serialization via `FastCall<T>` delegates
- Reliable channel with sequence numbers and ACK handling

## Recommended Architecture for Paralives Prototype

### Networking Layer (Isolated from Game Patches)
```
Networking/
├── NetworkManager.cs      # Abstract manager interface
├── TcpNetworkManager.cs   # TCP implementation (recommended for local-only)
├── PacketDispatcher.cs    # Message routing
├── MainThreadQueue.cs     # Thread-safe dispatch to Unity main thread
└── Messages/              # Message definitions
    ├── MessageBase.cs     # Abstract message class
    └── *.cs               # Concrete message types
```

### Game Integration Layer
```
Patches/
├── GameManagerPatches.cs  # Lifecycle control (menu, restart, scene loading)
├── PlayerPatches.cs       # Player state sync
├── EntityPatches.cs       # Entity spawn/despawn control
└── InputPatches.cs        # Input routing for remote players
```

### Session Management
```
Session/
├── MultiplayerSession.cs  # Static session state
└── LobbyManager.cs        # Local lobby management
```

### Key Design Decisions
1. **TCP over UDP** - Simpler implementation for local-only, no need for custom reliable delivery
2. **Message Registry Pattern** - Type-safe routing with string-based message codes
3. **MainThreadQueue** - Thread-safe dispatch from network threads to Unity main thread
4. **Postfix Patches First** - Prefer Postfix Harmony patches over Prefix for safety
5. **Heavy Logging** - All network events logged for debugging
6. **Clean-Room Implementation** - Extract patterns only, no code copying

## Next Steps
1. Implement networking layer skeleton
2. Implement message registry and dispatcher
3. Implement main thread queue
4. Add Harmony patches for game lifecycle control
5. Test with local host/client setup
