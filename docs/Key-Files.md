# Key Files Reference

## CupHeads/CupheadOnline (Primary Architecture Reference)

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `Plugin.cs` | BepInEx entry, config, Harmony patches | Plugin initialization pattern |
| `MultiplayerSession.cs` | Static session state | Singleton session management |
| `Net/MainThreadQueue.cs` | Thread-safe dispatch queue | Queue<Action> + lock pattern |
| `Net/NetManager.cs` | UDP networking manager | Reliable/unreliable delivery, handshake |
| `Net/PacketDispatcher.cs` | Packet routing | Switch-based type routing |
| `Net/Packets.cs` | Packet struct definitions | IPacket interface with Read/Write |

## plan-b-terraform-mods/FeatMultiplayer (Messaging Pattern Reference)

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `Plugin.cs` | Main entry, config, logging | Modular plugin architecture |
| `Plugin_Networking.cs` | TCP networking layer | TcpListener/TcpClient pattern |
| `Plugin_Messaging.cs` | Message registry and dispatch | Dictionary-based message routing |
| `MessageTypes/MessageBase.cs` | Abstract message class | Encode/TryDecode pattern |

## RavenM (Lifecycle Control Reference)

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `Plugin.cs` | BepInEx entry, mod restriction | Steamworks integration pattern |
| `IngameNetManager.cs` | Network manager with lifecycle patches | Host authority enforcement |
| `LobbySystem.cs` | Lobby management | Steam Matchmaking API usage |

## Architecture Summary

### Networking Approaches Compared

| Feature | CupHeads (UDP) | plan-b (TCP) | Recommended |
|---------|---------------|--------------|-------------|
| Protocol | Custom UDP | TCP | TCP (simpler for local-only) |
| Reliable Delivery | Manual ACK/retransmit | Built-in TCP | TCP built-in |
| Message Routing | Enum switch | String registry | String registry (flexible) |
| Thread Safety | Queue+lock | ConcurrentQueue | ConcurrentQueue |
| Serialization | BinaryWriter/Reader | BinaryWriter/Reader | Same pattern |

## Advanced Phase References (Phases 8-11)

### Live-Mode Player Sync Patterns

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `CupHeads/Sync/NetTick.cs` | Fixed tick rate engine | Tick-based sync with interpolation buffer |
| `CupHeads/Sync/RemotePlayer.cs` | Remote player state | Position/rotation sync with delta compression |
| `CupHeads/Sync/RemoteInputDriver.cs` | Input replication | Host-authoritative input routing |
| `RavenM/RavenM/ActorPacket.cs` | Actor state packet | Compact player state serialization |
| `RavenM/RavenM/NetActorController.cs` | Actor network control | Client prediction + server reconciliation |

### Save/Load and Session Patterns

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `CupHeads/Sync/SessionSync.cs` | Session state sync | Consistent snapshot capture pattern |
| `CupHeads/Sync/SaveSlotReplicator.cs` | Save slot sync | Coordinated save across clients |
| `RavenM/RavenM/LobbySystem.cs` | Lobby management | Session lifecycle with state persistence |

### Reconnection and Error Recovery Patterns

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `CupHeads/Net/NetManager.cs` | Connection management | Disconnect detection, reconnect logic |
| `CupHeads/Sync/ParticipantStatusTracker.cs` | Player status tracking | Graceful disconnect vs. network interruption |
| `RavenM/RavenM/IngameNetManager.cs` | Lifecycle-aware networking | Session cleanup on menu return/restart |

### Production Hardening Patterns

| File | Purpose | Pattern Extracted |
|------|---------|-------------------|
| `CupHeads/Diagnostics/BugReportExporter.cs` | Error reporting | Structured error logging with context |
| `LiteNetLib/ReliableChannel.cs` | Reliable delivery | Circuit breaker, ACK timeout patterns |
| `LiteNetLib/NetStatistics.cs` | Connection stats | Bandwidth/latency monitoring pattern |

### Design Principles Adopted
1. **Separation of Concerns** - Networking isolated from game patches
2. **Thread Safety** - Network threads never touch Unity objects directly
3. **Clean-Room** - Pattern extraction only, no code copying
4. **Heavy Logging** - All network events logged for debugging
5. **Postfix First** - Prefer Postfix Harmony patches over Prefix
6. **Host Authority** - Host validates all state changes; clients send intent only
7. **Graceful Degradation** - Feature toggles allow partial functionality when network is poor
8. **Tick-Based Everything** - All sync operates on fixed tick boundaries for consistency
