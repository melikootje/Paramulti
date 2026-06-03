using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using ParalivesMultiplayer.Input;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Performance;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Networking
{
    public class UdpNetworkManager : INetEventListener, IDisposable
    {
        public static UdpNetworkManager Instance { get; private set; }

        const int DefaultPort = 7890;
        const int ReliableChannel = 0;
        const int UnreliableChannel = 1;
        const int MaxConnectAttempts = 10;
        const int ReconnectDelay = 500;

        public bool IsRunning => _netManager != null && _netManager.IsRunning;
        public bool IsHost => _isHost;
        public bool IsClient => !_isHost && _netManager != null && _netManager.IsRunning;

        public Action<string> OnStatusChanged;
        public Action<ClientSession> OnClientConnected;
        public Action<int> OnClientDisconnected;

        NetManager _netManager;
        bool _isHost;
        NetPeer _serverPeer; // when client
        readonly ConcurrentDictionary<int, ClientSession> _sessions = new ConcurrentDictionary<int, ClientSession>();
        readonly ConcurrentDictionary<int, NetPeer> _peersById = new ConcurrentDictionary<int, NetPeer>();
        readonly ConcurrentQueue<PendingSend> _sendQueue = new ConcurrentQueue<PendingSend>();
        readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        Thread _sendThread;
        CancellationTokenSource _stopToken;
        int _nextClientId = 1;

        struct PendingSend
        {
            public NetPeer Peer;
            public byte[] Data;
            public int Offset;
            public int Length;
            public byte Channel;
            public DeliveryMethod Method;
        }

        public static void CreateSingleton()
        {
            if (Instance == null) Instance = new UdpNetworkManager();
        }

        public void StartHost(int port = DefaultPort)
        {
            if (IsRunning) return;
            _isHost = true;
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                DisconnectTimeout = 10000
            };
            _netManager.Start(port);
            _stopToken = new CancellationTokenSource();
            _sendThread = new Thread(SendLoop) { IsBackground = true, Name = "UdpSend" };
            _sendThread.Start();
            Log($"[Net] UDP Host listening on port {port}");
            OnStatusChanged?.Invoke($"Hosting on {port} (UDP)");
        }

        public void StartClient(string address, int port = DefaultPort)
        {
            if (IsRunning) return;
            _isHost = false;
            _netManager = new NetManager(this)
            {
                AutoRecycle = true,
                EnableStatistics = true,
                DisconnectTimeout = 10000
            };
            _netManager.Start();
            _netManager.Connect(address, port, "ParalivesMultiplayer");
            _stopToken = new CancellationTokenSource();
            _sendThread = new Thread(SendLoop) { IsBackground = true, Name = "UdpSend" };
            _sendThread.Start();
            Log($"[Net] UDP Client connecting to {address}:{port}");
            OnStatusChanged?.Invoke($"Connecting to {address}:{port} (UDP)");
        }

        public void Stop()
        {
            if (!IsRunning) return;
            try { _stopToken?.Cancel(); } catch { }
            _sendSignal.Set();
            try { _netManager?.Stop(); } catch { }
            _sendThread?.Join(500);
            _sendThread = null;
            _netManager = null;
            _serverPeer = null;
            _sessions.Clear();
            _peersById.Clear();
            while (_sendQueue.TryDequeue(out _)) { }
            OnStatusChanged?.Invoke("Disconnected");
            Log("[Net] UDP stopped");
        }

        public void SendToHost(MessageBase message)
        {
            if (!IsRunning || _serverPeer == null) return;
            var data = EncodeMessage(message);
            if (data == null) return;
            EnqueueSend(_serverPeer, data, GetChannelForMessage(message));
        }

        public void SendToClient(int clientId, MessageBase message)
        {
            if (!IsRunning || !_isHost) return;
            if (!_peersById.TryGetValue(clientId, out var peer)) return;
            var data = EncodeMessage(message);
            if (data == null) return;
            EnqueueSend(peer, data, GetChannelForMessage(message));
        }

        public void SendToAllClients(MessageBase message)
        {
            SendToAllExcept(-1, message);
        }

        public void SendToAllExcept(int exceptId, MessageBase message)
        {
            if (!IsRunning || !_isHost) return;
            var data = EncodeMessage(message);
            if (data == null) return;
            int channel = GetChannelForMessage(message);
            var method = (DeliveryMethod)channel;
            foreach (var kvp in _peersById)
            {
                if (kvp.Key == exceptId) continue;
                EnqueueSend(kvp.Value, data, channel);
            }
        }

        // Pick a channel based on message type.
        // Unreliable: position/animation (latest is most important, can drop)
        // Reliable: sync/control (must arrive)
        static int GetChannelForMessage(MessageBase msg)
        {
            switch (msg.MessageCode)
            {
                case "UpdateState":
                case "AnimationState":
                    return UnreliableChannel;
                default:
                    return ReliableChannel;
            }
        }

        // Encode a message using the same wire format as TcpNetworkManager:
        //   [4 bytes: totalLength] [1 byte: code length] [code bytes] [encoded body]
        // (the leading length is redundant over UDP since each packet is self-contained,
        // but we keep it for framing compatibility with any future hybrid code.)
        static byte[] EncodeMessage(MessageBase msg)
        {
            try
            {
                var code = msg.MessageCodeBytes;
                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms))
                {
                    // reserve 4 bytes for length
                    writer.Write(0);
                    writer.Write((byte)code.Length);
                    writer.Write(code);
                    var pos = ms.Position;
                    msg.Encode(writer);
                    writer.Flush();
                    var bodyLength = (int)(ms.Position - pos);
                    var totalLength = code.Length + bodyLength;

                    // write the real length at the head
                    var lenBuf = BitConverter.GetBytes(totalLength);
                    Array.Copy(lenBuf, 0, ms.GetBuffer(), 0, 4);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                LogWarning($"[Net] EncodeMessage failed for {msg.MessageCode}: {ex.Message}");
                return null;
            }
        }

        // The reverse: read framed message bytes into a MessageBase instance.
        // We delegate to MessageRegistry which knows the type by code prefix.
        static MessageBase DecodeMessage(byte[] data, int length)
        {
            if (length < 5) return null;
            int codeLen = data[4];
            if (length < 5 + codeLen) return null;
            var code = System.Text.Encoding.UTF8.GetString(data, 5, codeLen);
            if (!MessageRegistry.TryGetHandler(code, out var prototype)) return null;
            // Clone via MemberwiseClone (every message has a fresh instance) so we don't
            // mutate the shared prototype's fields when decoding.
            var msg = (MessageBase)System.Runtime.Serialization.FormatterServices
                .GetUninitializedObject(prototype.GetType());
            // Easier: just use the prototype's TryDecode pattern; the prototype's own
            // fields get reset by TryDecode for primitives and references.
            using (var ms = new MemoryStream(data, 5 + codeLen, length - 5 - codeLen))
            using (var reader = new BinaryReader(ms))
            {
                if (!prototype.TryDecode(reader, out var decoded)) return null;
                return decoded;
            }
        }

        void EnqueueSend(NetPeer peer, byte[] data, int channel)
        {
            if (peer == null) return;
            var method = channel == UnreliableChannel ? DeliveryMethod.Unreliable : DeliveryMethod.ReliableOrdered;
            _sendQueue.Enqueue(new PendingSend { Peer = peer, Data = data, Offset = 0, Length = data.Length, Channel = (byte)channel, Method = method });
            _sendSignal.Set();
        }

        void SendLoop()
        {
            while (_stopToken != null && !_stopToken.IsCancellationRequested)
            {
                _sendSignal.WaitOne(500);
                while (_sendQueue.TryDequeue(out var item))
                {
                    try
                    {
                        if (item.Peer != null && item.Peer.ConnectionState == ConnectionState.Connected)
                        {
                            item.Peer.Send(item.Data, item.Offset, item.Length, item.Method);
                            PacketStats.RecordSent(item.Length);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"[Net] Send failed: {ex.Message}");
                    }
                }
            }
        }

        // ===== INetEventListener =====

        public void OnPeerConnected(NetPeer peer)
        {
            Log($"[Net] Peer connected: {peer.Id} from {peer}");
            if (_isHost)
            {
                int clientId = Interlocked.Increment(ref _nextClientId);
                _peersById[clientId] = peer;
                var session = new ClientSession(clientId)
                {
                    ClientName = "Client_" + clientId
                };
                _sessions[clientId] = session;
                OnClientConnected?.Invoke(session);
            }
            else
            {
                _serverPeer = peer;
                Log("[Net] Connected to server");
                OnStatusChanged?.Invoke("Connected");

                // Announce ourselves to the host — mirrors TcpNetworkManager post-connect
                var connectMsg = new MsgConnect { ClientName = "LocalClient" };
                SendToHost(connectMsg);

                var joinMsg = new MsgPlayerJoin
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    PlayerName = $"Client_{MultiplayerSession.LocalPlayerId}"
                };
                SendToHost(joinMsg);
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Log($"[Net] Peer disconnected: {peer.Id}, reason={disconnectInfo.Reason}");
            if (_isHost)
            {
                foreach (var kvp in _peersById)
                {
                    if (kvp.Value == peer)
                    {
                        _peersById.TryRemove(kvp.Key, out _);
                        if (_sessions.TryRemove(kvp.Key, out var session))
                        {
                            session.DisconnectToken.Cancel();
                            OnClientDisconnected?.Invoke(kvp.Key);
                        }
                        break;
                    }
                }
            }
            else
            {
                _serverPeer = null;
                OnStatusChanged?.Invoke($"Disconnected: {disconnectInfo.Reason}");
            }
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            LogWarning($"[Net] Network error from {endPoint}: {socketError}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                int length = reader.GetInt();
                if (length <= 0 || length > 1024 * 1024) return;
                // Wire format: [4:totalLength(=codeLen+bodyLen)] [1:codeLen] [codeLen:code] [bodyLen:body]
                // totalLength excludes the 1-byte codeLen, so remaining data = 1 + totalLength bytes
                if (reader.AvailableBytes < 1 + length) return;
                var seg = reader.GetBytesSegment(1 + length);
                // Reconstruct the full frame [4:totalLength][1:codeLen][code][body] for DecodeMessage
                var data = new byte[4 + seg.Count];
                Buffer.BlockCopy(BitConverter.GetBytes(length), 0, data, 0, 4);
                Buffer.BlockCopy(seg.Array, seg.Offset, data, 4, seg.Count);
                var msg = DecodeMessage(data, data.Length);
                if (msg == null) return;
                PacketStats.RecordReceived(4 + seg.Count);
                msg.SenderClientId = peer.Id;
                HandleIncomingMessage(peer, msg);
            }
            catch (Exception ex)
            {
                LogWarning($"[Net] OnNetworkReceive failed: {ex.Message}");
            }
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

        public void PollEvents()
        {
            if (_netManager == null) return;
            _netManager.PollEvents();
        }

        public int ClientCount => _peersById.Count;
        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (_isHost)
            {
                request.AcceptIfKey("ParalivesMultiplayer");
            }
            else
            {
                request.Reject();
            }
        }

        // Map a peer to the integer client id used by the rest of the code.
        // (already mapped in OnPeerConnected; for client->server we treat server as id=0)
        int GetClientIdForPeer(NetPeer peer)
        {
            if (!_isHost) return 0;
            foreach (var kvp in _peersById)
            {
                if (kvp.Value == peer) return kvp.Key;
            }
            return 0;
        }

        // Dispatch the incoming message exactly the way TcpNetworkManager did:
        // - look up the typed message and run its case branch
        // - on the host, rebroadcast certain types to other clients
        void HandleIncomingMessage(NetPeer peer, MessageBase msg)
        {
            int senderClientId = GetClientIdForPeer(peer);
            msg.SenderClientId = senderClientId;

            // Mirrors TcpNetworkManager.HandleMessage switch verbatim, with the only change
            // being that host rebroadcasting uses the reliable channel.
            switch (msg)
            {
                case MsgConnect connect:
                    string safeName = MessageAuthenticator.SanitizePlayerName(connect.ClientName);
                    Log($"[Net][UDP] Client \"{safeName}\" connected (session {senderClientId})");
                    MultiplayerSession.OnClientConnected(senderClientId, safeName);

                    if (_isHost)
                    {
                        var hostJoin = new MsgPlayerJoin
                        {
                            PlayerId = 0,
                            PlayerName = "Host"
                        };
                        SendToClient(senderClientId, hostJoin);

                        var ids = MultiplayerSession.GetPlayerIds();
                        var names = new string[ids.Length];
                        for (int i = 0; i < ids.Length; i++)
                            MultiplayerSession.TryGetPlayerName(ids[i], out names[i]);

                        var roster = new MsgRosterSync
                        {
                            PlayerIds = ids,
                            PlayerNames = names
                        };
                        SendToClient(senderClientId, roster);

                        var hostCharData = Session.RemoteCharacterManager.BuildLocalCharacterDataSync();
                        if (hostCharData != null)
                        {
                            SendToClient(senderClientId, hostCharData);
                            Log($"[Paramulti][Network][UDP] Sent host character data to new client {senderClientId}");
                        }
                    }
                    break;

                case MsgDisconnect disconnect:
                    Log($"[Net][UDP] Disconnect received: {disconnect.Reason}");
                    break;

                case MsgPlayerJoin join:
                    Log($"[Paramulti][Network][UDP] Received MsgPlayerJoin for Player {join.PlayerId} (Name={join.PlayerName}).");
                    MultiplayerSession.OnPlayerJoined(join.PlayerId, join.PlayerName);

                    if (_isHost)
                    {
                        SendToAllExcept(senderClientId, join);
                        SendToClient(senderClientId, join);
                    }

                    if (!_isHost && join.PlayerId == MultiplayerSession.LocalPlayerId)
                    {
                        var myData = Session.RemoteCharacterManager.BuildLocalCharacterDataSync();
                        if (myData != null)
                        {
                            SendToHost(myData);
                            Log($"[Paramulti][Network][UDP] Sent local character data to host");
                        }
                    }
                    break;

                case MsgPlayerLeave leave:
                    Log($"[Net][UDP] Player left: ID={leave.PlayerId}");
                    MultiplayerSession.OnPlayerLeft(leave.PlayerId);
                    if (_isHost)
                        SendToAllExcept(senderClientId, msg);
                    break;

                case MsgUpdateState update:
                    Plugin.Log.LogInfo($"[Net][UDP] UpdateState from player {update.PlayerId} at {update.Position}, tick={update.Tick}");
                    Session.PlayerSyncManager.EnqueueState(update);
                    if (_isHost)
                        SendToAllExcept(senderClientId, update);
                    break;

                case MsgChat chat:
                    chat.Message = MessageAuthenticator.SanitizeChatMessage(chat.Message);
                    if (MessageAuthenticator.ContainsInjection(chat.Message))
                    {
                        ErrorRecoveryManager.RecordError(senderClientId, "InjectionAttempt");
                        LogWarning($"[Net][UDP] Chat injection attempt from client {senderClientId} blocked");
                        return;
                    }
                    ChatManager.HandleChat(chat);
                    break;

                case MsgCursorPing ping:
                    Log($"[Net][UDP] CursorPing from player {ping.PlayerId} at {ping.Position}");
                    Session.BuildCursorSyncManager.ReceiveCursorPing(ping);
                    if (_isHost)
                        SendToAllExcept(senderClientId, ping);
                    break;

                case MsgAnimationState anim:
                    Log($"[Net][UDP] AnimationState from player {anim.PlayerId}: hash={anim.AnimatorStateHash}, time={anim.NormalizedTime:F2}");
                    Session.AnimationSyncManager.ReceiveAnimationState(anim);
                    if (_isHost)
                        SendToAllExcept(senderClientId, anim);
                    break;

                case MsgBuildObjectPlaced build:
                    Log($"[Net][UDP] BuildObjectPlaced: type={build.ObjectTypeId}, pos={build.Position}, seq={build.SequenceNumber}");
                    if (_isHost)
                    {
                        Log("[Net][UDP] Host rebroadcasting BuildObjectPlaced to all clients.");
                        SendToAllExcept(senderClientId, build);
                    }
                    break;

                case MsgEntitySpawn spawn:
                    Log($"[Net][UDP] EntitySpawn: id={spawn.EntityId}, type={spawn.EntityType}, pos={spawn.Position}");
                    Session.EntitySyncManager.ApplyRemoteSpawn(spawn);
                    if (_isHost)
                        SendToAllExcept(senderClientId, spawn);
                    break;

                case MsgEntityDespawn despawn:
                    Log($"[Net][UDP] EntityDespawn: id={despawn.EntityId}");
                    Session.EntitySyncManager.ApplyRemoteDespawn(despawn);
                    if (_isHost)
                        SendToAllExcept(senderClientId, despawn);
                    break;

                case MsgRequestFullState reqState:
                    Log($"[Net][UDP] RequestFullState from player {reqState.PlayerId}");
                    if (_isHost)
                    {
                        var snapshot = Session.EntitySyncManager.BuildSnapshot();
                        SendToClient(senderClientId, snapshot);
                    }
                    break;

                case MsgFullStateSnapshot snap:
                    Log($"[Net][UDP] FullStateSnapshot: tick={snap.Tick}, entities={snap.Entities.Count}");
                    Session.EntitySyncManager.ApplyFullStateSnapshot(snap);
                    break;

                case MsgReadyCheck ready:
                    Log($"[Net][UDP] ReadyCheck: player {ready.PlayerId} is {(ready.IsReady ? "READY" : "NOT READY")}");
                    Session.LobbyManager.HandleRemoteReady(ready);
                    break;

                case MsgInputCommand inputCmd:
                    Log($"[Net][UDP] InputCommand from player {inputCmd.PlayerId}: action={inputCmd.Action}");
                    InputRouter.ProcessRemoteInput(inputCmd);
                    if (_isHost)
                        SendToAllExcept(senderClientId, inputCmd);
                    break;

                case MsgBuildModeEvent buildEvt:
                    Log($"[Net][UDP] BuildModeEvent: type={buildEvt.EventType}, entity={buildEvt.EntityId}, player={buildEvt.PlayerId}");
                    if (_isHost)
                                    BuildSyncManager.ValidateAndApply(buildEvt);
                    else
                        BuildSyncManager.ValidateAndApply(buildEvt);
                    break;

                case MsgHeartbeat hb:
                    Log($"[Net][UDP] MsgHeartbeat: sender={senderClientId}, playerId={hb.PlayerId}, tick={hb.Tick}");
                    if (_isHost)
                    {
                        DesyncDetector.ProcessHeartbeat(hb);
                        SendToAllExcept(senderClientId, hb);
                    }
                    break;

                case MsgCharacterDataSync charData:
                    Log($"[Net][UDP] CharacterDataSync from player {charData.PlayerId}: GUID={charData.CharacterGuid:X}");
                    Session.RemoteCharacterManager.ApplyRemoteCharacterDataSync(charData);
                    if (_isHost)
                        SendToAllExcept(senderClientId, charData);
                    break;

                case MsgSelectCharacter selectChar:
                    Log($"[Net][UDP] SelectCharacter from player {selectChar.PlayerId}: GUID={selectChar.CharacterGuid:X}, selected={selectChar.Selected}");
                    if (_isHost)
                        SendToAllExcept(senderClientId, selectChar);
                    break;

                case MsgTimeSync timeSync:
                    TimeSyncManager.Apply(timeSync);
                    if (_isHost)
                        SendToAllExcept(senderClientId, timeSync);
                    break;

                default:
                    Log("[Net][UDP] Unhandled message: " + msg.GetType().Name);
                    break;
            }
        }

        public UdpNetworkManager()
        {
            Instance = this;
        }

        public void Dispose()
        {
            Stop();
            if (Instance == this) Instance = null;
        }

        static void Log(string msg) => Plugin.Log.LogInfo(msg);
        static void LogWarning(string msg) => Plugin.Log.LogWarning(msg);
    }
}
