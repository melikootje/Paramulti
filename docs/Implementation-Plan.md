# Phased Implementation Plan

## Phase 1: Networking Skeleton (Week 1-2)
**Goal:** Establish basic networking infrastructure with message passing

### Milestone 1.1: Project Structure
- [ ] Create solution structure with proper namespaces
- [ ] Set up BepInEx plugin entry point
- [ ] Configure Harmony instance
- [ ] Add logging infrastructure

### Milestone 1.2: Main Thread Queue
- [ ] Implement `MainThreadQueue` pattern from CupHeads reference
- [ ] Test thread-safe enqueue/dequeue operations
- [ ] Integrate with Unity main thread via `Plugin.Update()`

### Milestone 1.3: TCP Network Manager
- [ ] Implement `NetworkManager` abstract interface
- [ ] Implement `TcpNetworkManager` with:
  - Host mode: TcpListener accepting connections
  - Client mode: TcpClient connecting to host
  - Sender/Receiver loops on background threads
  - ConcurrentQueue for thread-safe message passing
- [ ] Implement basic connection/disconnection handling

### Milestone 1.4: Message Registry
- [ ] Implement `MessageBase` abstract class
- [ ] Create message registry pattern (Dictionary<string, MessageBase>)
- [ ] Implement length-prefixed binary protocol
- [ ] Add extension methods for Vector3, Quaternion serialization

## Phase 2: Session Management (Week 3-4)
**Goal:** Host/client session lifecycle and basic state sync

### Milestone 2.1: Session State
- [ ] Implement `MultiplayerSession` static class
- [ ] Add session state flags (IsActive, IsHost, IsClient)
- [ ] Implement tick counter for synchronization

### Milestone 2.2: Lobby System
- [ ] Create local lobby management
- [ ] Implement player join/leave handling
- [ ] Add ready-check mechanism

### Milestone 2.3: Basic Messages
- [ ] Define core message types:
  - `MessageConnect` / `MessageDisconnect`
  - `MessagePlayerJoin` / `MessagePlayerLeave`
  - `MessageSyncState` (initial state snapshot)
  - `MessageUpdateState` (delta updates)

## Phase 3: Game Integration (Week 5-6)
**Goal:** Harmony patches for game lifecycle control

### Milestone 3.1: Lifecycle Patches
- [ ] Patch scene loading to sync across players
- [ ] Patch menu returns to disconnect all players
- [ ] Patch level restarts to enforce host authority
- [ ] Patch player spawning/despawning

### Milestone 3.2: Player State Sync
- [ ] Implement player position/rotation sync
- [ ] Add input routing for remote players
- [ ] Handle player state updates at fixed tick rate

### Milestone 3.3: Entity Sync
- [ ] Implement entity spawn/despawn messages
- [ ] Add entity state synchronization
- [ ] Handle entity lifecycle events

## Phase 4: Polish and Testing (Week 7-8)
**Goal:** Debug overlay, performance optimization, testing

### Milestone 4.1: Debug Overlay
- [ ] Add connection status HUD
- [ ] Show ping/latency metrics
- [ ] Display packet statistics
- [ ] Add debug logging toggle

### Milestone 4.2: Performance
- [ ] Profile network throughput
- [ ] Optimize message serialization
- [ ] Implement delta compression for state updates
- [ ] Add bandwidth limiting

### Milestone 4.3: Testing
- [ ] Test host/client scenarios
- [ ] Test connection/disconnection edge cases
- [ ] Verify no desync in game state
- [ ] Load test with multiple clients

## Phase 7: Host-Authoritative Build Sync & Live Testing (Week 9-10)
**Goal:** Host-authoritative validation, remote build change application, and integrated testing

### Milestone 7.1: BuildSyncManager
- [ ] Implement `BuildSyncManager` for host-authoritative build event validation
- [ ] Add sequence number tracking per player to detect out-of-order/duplicate events
- [ ] Implement dry-run mode (log-only) and real-apply mode (reflect in game state)
- [ ] Add config toggles: `EnableBuildSyncDryRun`, `EnableBuildSyncRealApply`

### Milestone 7.2: Remote Build Change Application
- [ ] Wire `MsgBuildModeEvent` handler to apply remote changes via `BuildSyncManager`
- [ ] Implement rollback/error handling for failed remote builds
- [ ] Add desync detection for build events (tick validation, sequence gap monitoring)
- [ ] Host validates and rebroadcasts accepted build events

### Milestone 7.3: Integrated Testing
- [ ] Test host/client build event propagation
- [ ] Test dry-run mode (events logged but not applied)
- [ ] Test real-apply mode (events reflected in remote entity state)
- [ ] Test desync recovery after missed heartbeats
- [ ] Verify no game crashes from failed remote builds

## Technical Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Game updates break patches | High | Use flexible patch targeting, monitor for breaking changes |
| Network latency causes desync | Medium | Implement tick-based sync with interpolation |
| Thread safety issues | High | Use ConcurrentQueue, lock-free patterns where possible |
| Performance bottlenecks | Medium | Profile early, optimize serialization |

## Success Criteria
- [ ] Host can start a session and accept client connections
- [ ] Clients can join host session and see synchronized game state
- [ ] No desync between host and clients during gameplay
- [ ] Clean disconnect/reconnect handling
- [ ] Debug overlay shows connection status and metrics
- [ ] All Harmony patches work without game crashes


Prompt 2 — make Qwen analyze existing multiplayer mods

Use this after cloning the reference repos:

I have cloned these public reference repos locally:

- reference-mods/CupHeads
- reference-mods/RavenM
- reference-mods/plan-b-terraform-mods
- reference-mods/LiteNetLib

Analyze them as reference material only. Do not copy code.

I want to build my own clean-room BepInEx multiplayer prototype for a Unity life-sim/build-mode game.

Find and summarize:

1. BepInEx plugin entry points
2. Harmony patch structure
3. networking initialization
4. packet/message definitions
5. host/client authority model
6. scene sync strategy
7. object/entity sync strategy
8. thread-safe queues or main-thread dispatching
9. logging/debug overlay patterns
10. build system and DLL reference layout
11. license risks
12. patterns reusable for my Paralives prototype
13. patterns that are too game-specific and should not be reused

Output:
- a concise architecture summary
- a list of files/classes worth reading first
- a recommended clean-room architecture for my own mod
- a phased implementation plan

Save its answer as:

docs/REFERENCE_ANALYSIS.md
Prompt 3 — ask Qwen to design your architecture from the references
Based on the reference analysis, design a clean-room architecture for my BepInEx multiplayer prototype.

Scope:
- Unity game
- BepInEx plugin
- Harmony/HarmonyX patches
- local/LAN multiplayer first
- no game-state mutation at first
- build toward shared build-mode sync
- do not attempt full live simulation yet

Milestones:
1. plugin loads
2. debug GUI
3. LAN chat
4. player presence
5. read-only Harmony logging
6. shared cursor/ping markers
7. read-only build-mode event observation
8. object placement message schema
9. host-authoritative build-mode sync prototype
10. limited live-mode sync much later

Produce:
- folder structure
- class/module responsibilities
- message/packet types
- host/client authority rules
- threading model
- logging strategy
- config options
- test plan
- what to avoid

Save this as:

docs/ARCHITECTURE.md
Prompt 4 — generate the first BepInEx plugin
Create the smallest possible BepInEx plugin for this project.

Requirements:
- C#
- BepInEx plugin entry class
- logs from Awake()
- no Harmony patches yet
- no networking yet
- no game-specific references
- include basic config values:
  - EnableDebugGui
  - LogLevel
  - PlayerName
- include a simple project folder structure
- include .csproj guidance
- include which DLLs I need to reference from BepInEx and the Unity game
- keep it minimal and buildable

Return complete files only.

After building and testing, paste the compiler/runtime errors back into Qwen.

Prompt 5 — add debug GUI
Extend the plugin with a simple Unity OnGUI debug window.

Requirements:
- movable window
- status label
- player name field
- Host button
- Join button
- Disconnect button
- text input for IP address
- last 30 log messages visible in the window
- no actual networking yet
- all actions should only log messages for now
- keep GUI code separate from plugin bootstrap code

Return only changed/new files.
Prompt 6 — add LAN chat using simple networking

Use LiteNetLib as the first serious networking candidate because it is a C# reliable UDP library for Mono/.NET and is MIT licensed.

Add a minimal LAN chat networking layer.

Use LiteNetLib unless there is a clear reason not to.

Requirements:
- Host button starts a local server
- Join button connects to host IP
- Disconnect button safely shuts down
- clients can send chat messages
- host rebroadcasts chat messages
- use reliable ordered delivery
- network polling must not block Unity main thread
- use a thread-safe queue to pass messages to Unity main thread
- show chat messages in the debug GUI
- add heavy logging
- no game-state sync yet
- no Harmony patches yet

Architecture requirements:
- Networking code must be separate from GUI
- Message serialization must be separate from transport
- Add message types/enums now so later game sync can reuse them

Return complete changed/new files.
Prompt 7 — make Qwen review its own code before you run it

Use this after every implementation prompt:

Review the code you just produced.

Look specifically for:
- compile errors
- missing using statements
- Unity lifecycle mistakes
- unsafe thread access to Unity objects
- networking thread shutdown bugs
- memory leaks
- null reference risks
- bad separation of concerns
- overengineering
- anything that assumes game-specific classes

Return:
1. issues found
2. minimal fixes
3. files to change
4. final corrected code only where needed
Prompt 8 — build/run error loop

When it fails, paste errors with this:

The latest build/run failed.

Current milestone:
[paste milestone]

Compiler errors:
[paste full errors]

BepInEx log:
[paste relevant BepInEx log]

Unity/player log if available:
[paste relevant log]

Current files:
[paste relevant files]

Task:
Diagnose the root cause. Do not rewrite everything.
Give me:
1. most likely cause
2. smallest fix
3. exact file changes
4. extra logging to add if uncertain

Use this loop until the milestone works.

Prompt 9 — inspect reference repos for specific patterns

Use this when you want to learn from CupHeads/RavenM/Plan B without copying:

Inspect the cloned reference repos for this specific pattern:

Pattern to study:
[host authority / scene sync / packet schema / debug overlay / main-thread queue / object sync]

Repos:
- reference-mods/CupHeads
- reference-mods/RavenM
- reference-mods/plan-b-terraform-mods

Do not copy code.

Return:
1. where each repo implements the pattern
2. what problem the pattern solves
3. what parts are game-specific
4. what clean-room version I should implement
5. pitfalls for a Unity life-sim/build-mode game
Prompt 10 — start Harmony only after networking works

BepInEx includes HarmonyX among its used libraries, and HarmonyX targets BepInEx/Unity runtime patching, so use it only after your plugin, GUI, and LAN chat are stable.

Add Harmony/HarmonyX support to the plugin, but only for safe read-only logging.

Requirements:
- create a Harmony instance during plugin startup
- patch nothing game-specific yet
- add structure for optional patch classes
- add config toggle EnableHarmonyPatches
- add safe try/catch around patch registration
- log all patch registration results
- include a placeholder example patch only if it targets a harmless Unity/BepInEx-safe method
- do not assume Paralives class names

Return changed/new files only.
Prompt 11 — after you inspect Paralives assemblies

Do this locally only:

I inspected the game assemblies locally. Here are relevant class names and method signatures.

Do not invent missing APIs.
Do not mutate game state yet.
Suggest safe read-only Harmony Postfix hooks.

Known signatures:
[paste small list of discovered class/method signatures]

I want to observe:
- scene loaded
- current mode changed
- selected object changed
- object placed
- object deleted
- object moved
- object recolored
- save loaded
- save started

For each suggested hook:
1. class and method to patch
2. why it is likely useful
3. what data to log
4. risks
5. safe Postfix code pattern
6. how to disable via config
Prompt 12 — convert observations into network messages
Based on these observed build-mode events, design network message schemas.

Observed events:
[paste logs from read-only Harmony patches]

Design messages for:
- PlayerPresence
- Chat
- CursorPing
- BuildObjectPlaced
- BuildObjectMoved
- BuildObjectDeleted
- BuildObjectStyleChanged
- RequestFullState
- FullStateSnapshot
- DesyncWarning

Requirements:
- host-authoritative model
- versioned message format
- compact but readable serialization
- include object identity strategy
- include timestamps/sequence numbers
- include validation rules
- do not apply remote changes yet
Prompt 13 — implement one sync feature only

Do not ask for full build-mode sync. Start with a harmless feature like cursor pings.

Implement only shared cursor/ping markers.

Requirements:
- pressing a debug GUI button sends a CursorPing message
- host rebroadcasts it
- clients display the ping in the debug GUI/log first
- do not spawn game objects yet unless using a simple temporary Unity debug marker
- no build-mode object sync yet
- no save changes
- heavy logging
- include desync-safe handling for unknown/old messages

Return changed/new files only.

Then move to build objects later.

Prompt 14 — object placement dry-run
Implement BuildObjectPlaced sync in dry-run mode only.

Inputs:
- observed object placement logs
- current message schema
- current networking code

Requirements:
- when local object placement is observed, create a BuildObjectPlaced message
- send it to host
- host validates and rebroadcasts
- receiving clients log the event only
- do not spawn or modify remote game objects yet
- include object ID, prefab/type ID if available, position, rotation, style/color if available
- include sequence number
- include source player ID
- add config option EnableBuildSyncDryRun

Return changed/new files only.
Prompt 15 — applying remote build changes

Only after dry-run logs are correct:

Based on these local class/method signatures, suggest the safest way to apply a remote build-mode object placement.

Known signatures:
[paste exact signatures]

Observed dry-run messages:
[paste logs]

Rules:
- prefer calling existing game placement methods
- do not construct internal objects manually unless unavoidable
- do not modify save files directly
- add dry-run and real-apply modes
- add desync detection
- add rollback/error handling
- add detailed logging
- produce a minimal implementation for one object type first
Prompt 16 — end-of-session handoff

At the end of every session, ask Qwen:

Create a concise project handoff summary.

Include:
1. current milestone
2. what works
3. what does not work
4. files changed
5. important design decisions
6. current bugs
7. latest logs/errors
8. next safest step
9. prompts I should use next
10. things not to do yet

Write this as docs/CURRENT_STATE.md.

Commit after each working milestone:

git add .
git commit -m "Milestone: LAN chat works"