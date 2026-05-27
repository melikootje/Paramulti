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

### Design Principles Adopted
1. **Separation of Concerns** - Networking isolated from game patches
2. **Thread Safety** - Network threads never touch Unity objects directly
3. **Clean-Room** - Pattern extraction only, no code copying
4. **Heavy Logging** - All network events logged for debugging
5. **Postfix First** - Prefer Postfix Harmony patches over Prefix
