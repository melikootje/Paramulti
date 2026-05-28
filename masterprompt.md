You are working in the GitHub repo `melikootje/Paramulti`, a BepInEx/Harmony multiplayer mod for Paralives.

Critical requirement:
Do not implement this only as a generic Unity proxy system. First inspect the actual Paralives game assemblies/classes at runtime and wire into the game’s own character, household, selection, spawning, controller, and save/entity systems wherever possible.

The mod must look at the game’s own code/API using reflection and Harmony:
- Enumerate loaded assemblies such as `Assembly-CSharp`, `Paralives*`, and Unity assemblies.
- Discover real types related to characters/Parafolk, households, controllable agents, player controllers, selection managers, save entities, world/entity spawning, and transforms.
- Log discovered candidate types, methods, fields, and properties.
- Prefer using the game’s own character creation/spawn/registration systems instead of creating disconnected Unity primitives.
- Only use fallback proxy GameObjects when no safe game-native path can be found.

Goal:
Make connected players automatically appear as real in-game characters where possible, using Paralives’ own systems, without requiring players to manually add those characters beforehand.

Implementation requirements:
1. Add a runtime discovery layer, for example `ParalivesGameApiResolver`, that scans loaded game assemblies for likely character/entity APIs.
2. Add a `RemoteCharacterManager` that uses the resolved game API to:
   - find the local controlled character,
   - create or clone a remote character through the game’s own systems,
   - register that character with the same managers the base game uses,
   - map `playerId -> character/entity/transform`.
3. If no safe game-native spawn path exists, fall back to a visible proxy, but keep this clearly logged as fallback behavior.
4. Subscribe to `PlayerSyncManager.OnRemotePlayerRender` and apply remote movement to the game-native character transform when available.
5. Fix `PlayerStatePatches` so it correctly extracts transforms from `Component`, `GameObject`, or reflected `transform` members.
6. Implement join/roster synchronization so all clients know about already-connected players and can auto-create/assign remote characters.
7. Ensure local input only controls the local character; remote characters should be driven by network state or routed remote input, not local input.
8. Add logs showing:
   - discovered game classes,
   - selected spawn/character API,
   - local character found,
   - remote game-native character created,
   - fallback proxy created,
   - state sent/received/applied,
   - cleanup on disconnect.

Acceptance criteria:
- Host presses F5, client presses F6.
- Both sides automatically see a character for the other player.
- The implementation uses Paralives’ own character/entity systems when discoverable.
- No manual character pre-adding is required.
- Remote movement is applied to the matching remote character.
- If game-native APIs are not discoverable, fallback proxies still appear and move.
- The code builds with `dotnet build src/ParalivesMultiplayer/ParalivesMultiplayer.csproj -c Release`.

Do not hardcode unverified Paralives class names as the only path. Use discovery, reflection, graceful fallback, and detailed logging.

# Role & Goal
You are an expert C# gameplay programmer specializing in Unity-based multiplayer architecture, reverse-engineering, and networking mods. Your goal is to completely implement the multi-client character synchronization logic for the `ParalivesMultiplayer` mod using the existing file architecture (`LobbyManager.cs`, `TcpNetworkManager.cs`, `PlayerSyncManager.cs`, and `RemoteCharacterManager.cs`).

---

# Execution Steps

## 1. Implement Network Roster Handshake (`LobbyManager.cs` / `TcpNetworkManager.cs`)
* Modify the host-side packet processing for `MsgPlayerJoin`. 
* When a new player connects:
  1. **Broadcast Arrival:** Wrap the new player's ID and session data into a packet and broadcast it to all *currently connected* clients.
  2. **Deliver Snapshot:** Compile a list of all existing players currently in the lobby and send it as a dedicated roster snapshot (or sequential join packets) *exclusively* to the newly connected client.

## 2. Implement Proxy Routing & Update Loop (`PlayerSyncManager.cs`)
* Ensure your network update packets (`MsgUpdateState`) contain a distinct `PlayerID` (or `ulong` identifier) payload header.
* Wire `OnRemotePlayerRender` or your main incoming packet handler to read this ID.
* Maintain an internal tracking dictionary: `private Dictionary<ulong, GameObject> _remoteProxies = new();`.
* When a transform packet arrives:
  1. Look up the `PlayerID` in `_remoteProxies`. If it doesn't exist, ignore or log silently.
  2. If found, extract the position and rotation data. Do not violently snap the position; pass the coordinates to an interpolation helper to smoothly slide the GameObject to its target destination to eliminate visual jitter.

## 3. Implement Isolated Proxy Spawning (`RemoteCharacterManager.cs`)
* Write a robust character spawning routine. If native game reflection fails, immediately fall back to instantiating a basic fallback primitive (such as a Capsule or Cube) with a visible text mesh label representing the player's name.
* **CRITICAL - Prevent Input Hijacking:** Immediately after spawning any proxy character (native or fallback), you must strip or disable all local control components. Loop through the spawned components and disable anything matching `Input`, `Controller`, `Camera`, or `Motor`. The remote character must be entirely kinematic and driven strictly by incoming network positions.

## 4. Add Diagnostic Logging
Sprinkle explicit, distinct diagnostic console logs prefixing `[Paramulti]` so developers can track the data flow in real-time:
* `[Paramulti][Local] Captured local avatar transform. Sending state update.`
* `[Paramulti][Network] Received MsgPlayerJoin for Player {id}.`
* `[Paramulti][ProxyManager] Spawning proxy GameObject for Player {id} (Fallback: {true/false}).`
* `[Paramulti][Sync] Applying transform interpolation for Player {id}.`

---

# Guardrails & Output Expectations
1. Write clean, defensive C# code with thorough null-checks (especially when retrieving objects from dictionaries or interacting with native game variables via reflection).
2. Do not delete or overwrite pre-existing networking hooks or socket management code—instead, build cleanly upon them.
3. Provide the full, updated source code or explicit patches for the affected files.