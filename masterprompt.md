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