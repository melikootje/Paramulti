using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ParalivesMultiplayer.Input;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Performance;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Networking
{
    public class ClientSession
    {
        public readonly int Id;
        public string ClientName;
        public bool IsAuthenticated;
        public TcpClient TcpClient;
        public readonly ConcurrentQueue<MessageBase> SenderQueue = new ConcurrentQueue<MessageBase>();
        public readonly AutoResetEvent SendSignal = new AutoResetEvent(false);
        public readonly CancellationTokenSource DisconnectToken = new CancellationTokenSource();

        public ClientSession(int id)
        {
            Id = id;
        }

        public void Send(MessageBase message, bool signal = true)
        {
            if (DisconnectToken.IsCancellationRequested) return;
            SenderQueue.Enqueue(message);
            if (signal)
                SendSignal.Set();
        }
    }

    public class TcpNetworkManager : IDisposable
    {
        public static TcpNetworkManager Instance { get; private set; }

        const int DefaultPort = 7890;
        const int SendBufferSize = 1024 * 1024;
        const int LoopWakeupMs = 500;

        public bool IsRunning => _stopNetwork == null || !_stopNetwork.IsCancellationRequested;

        static readonly BandwidthLimiter BandwidthLimit = new BandwidthLimiter(8 * 1024 * 1024, 16 * 1024 * 1024);
        public bool IsHost { get; private set; }
        public bool IsConnected => IsHost ? _sessions.Count > 0 : _hostSession != null && !_hostSession.DisconnectToken.IsCancellationRequested;

        readonly Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
        readonly ConcurrentQueue<MessageBase> _receiverQueue = new ConcurrentQueue<MessageBase>();
        readonly HashSet<int> _loggedFirstReceive = new HashSet<int>();
        static int _nextClientId;

        CancellationTokenSource _stopNetwork;
        CancellationTokenSource _stopHostAcceptor;
        TcpListener _listener;
        ClientSession _hostSession;

        public event Action<string> OnStatusChanged;
        public event Action<ClientSession, MessageBase> OnMessageReceived;

        public TcpNetworkManager()
        {
            if (Instance != null)
                throw new InvalidOperationException("TcpNetworkManager is a singleton.");
            Instance = this;
        }

        public void StartHost(int port = DefaultPort)
        {
            Shutdown();
            IsHost = true;
            _stopNetwork = new CancellationTokenSource();
            _stopHostAcceptor = new CancellationTokenSource();

            var task = System.Threading.Tasks.Task.Factory.StartNew(() => HostAcceptor(port),
                _stopNetwork.Token, System.Threading.Tasks.TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Log($"[Net] Starting host on port {port}");
            StatusChanged("Hosting on port " + port);
        }

        public void StartClient(string address, int port = DefaultPort)
        {
            Shutdown();
            IsHost = false;
            _stopNetwork = new CancellationTokenSource();

            System.Threading.Tasks.Task.Run(() => ClientRunner(address, port));
        }

        void HostAcceptor(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                Log($"[Net] Listening on port {port}");

                while (!_stopNetwork.IsCancellationRequested && !_stopHostAcceptor.IsCancellationRequested)
                {
                    try
                    {
                        var client = _listener.AcceptTcpClient();
                        AcceptClient(client);
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (!_stopNetwork.IsCancellationRequested)
                            LogError($"[Net] Accept error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stopNetwork.IsCancellationRequested)
                    LogError($"[Net] Host acceptor error: {ex.Message}");
            }
            finally
            {
                try { _listener?.Stop(); } catch { }
                _listener = null;
                Log("[Net] Host acceptor stopped.");
            }
        }

        void AcceptClient(TcpClient client)
        {
            var sessionId = Interlocked.Increment(ref _nextClientId);
            var session = new ClientSession(sessionId);
            session.TcpClient = client;
            _sessions[sessionId] = session;

            Log($"[Net] Client connected from {client.Client.RemoteEndPoint} (session {sessionId})");
            StatusChanged($"Client connected: {client.Client.RemoteEndPoint}");

            System.Threading.Tasks.Task.Factory.StartNew(() => ReceiverLoop(session),
                _stopNetwork.Token, System.Threading.Tasks.TaskCreationOptions.LongRunning, TaskScheduler.Default);
            System.Threading.Tasks.Task.Factory.StartNew(() => SenderLoop(session),
                _stopNetwork.Token, System.Threading.Tasks.TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        void ClientRunner(string address, int port)
        {
            Log($"[Net] Connecting to {address}:{port}...");
            StatusChanged("Connecting to " + address + ":" + port);

            try
            {
                var client = new TcpClient();
                client.Connect(address, port);
                _hostSession = new ClientSession(0);
                _hostSession.TcpClient = client;

                Log("[Net] Connected to host.");
                StatusChanged("Connected");

                System.Threading.Tasks.Task.Factory.StartNew(() => ReceiverLoop(_hostSession),
                    _stopNetwork.Token, System.Threading.Tasks.TaskCreationOptions.LongRunning, TaskScheduler.Default);
                System.Threading.Tasks.Task.Factory.StartNew(() => SenderLoop(_hostSession),
                    _stopNetwork.Token, System.Threading.Tasks.TaskCreationOptions.LongRunning, TaskScheduler.Default);

                var connectMsg = new MsgConnect { ClientName = "LocalClient" };
                SendToHost(connectMsg);

                var joinMsg = new MsgPlayerJoin
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    PlayerName = $"Client_{MultiplayerSession.LocalPlayerId}"
                };
                SendToHost(joinMsg);
            }
            catch (Exception ex)
            {
                LogError($"[Net] Connection failed: {ex.Message}");
                StatusChanged("Connection failed: " + ex.Message);

                var err = new MsgDisconnect { Reason = "ConnectionRefused" };
                _receiverQueue.Enqueue(err);
            }
        }

        void SenderLoop(ClientSession session)
        {
            var tcpClient = session.TcpClient;
            if (tcpClient == null) return;

            Stream stream = null;
            try
            {
                stream = tcpClient.GetStream();
            }
            catch
            {
                return;
            }

            LogDebug($"[Net] SenderLoop started for session {session.Id}");

            try
            {
                var buffer = new MemoryStream(SendBufferSize);
                var writer = new BinaryWriter(buffer, Encoding.UTF8);

                while (!_stopNetwork.IsCancellationRequested && !session.DisconnectToken.IsCancellationRequested)
                {
                    if (session.SenderQueue.TryDequeue(out var msg))
                    {
                        if (msg is MsgDisconnect)
                            break;

                        ClearStream(buffer);
                        var code = msg.MessageCodeBytes;
                        writer.Write(0);
                        writer.Write((byte)code.Length);
                        writer.Write(code);
                        var pos = buffer.Position;
                        msg.Encode(writer);
                        writer.Flush();
                        var msgLength = (int)(buffer.Position - pos);
                        var totalLength = code.Length + msgLength;

                        if (!BandwidthLimit.TryConsume(totalLength + 5))
                        {
                            LogDebug($"[Net] Bandwidth limit hit, dropping message {msg.MessageCode} ({totalLength + 5} bytes)");
                            continue;
                        }

                        buffer.Position = 0;
                        writer.Write(totalLength);
                        writer.Flush();

                        buffer.Position = 0;
                        buffer.WriteTo(stream);
                        stream.Flush();
                        buffer.Position = 0;

                        PacketStats.RecordSent(totalLength + 5);
                        LogDebug($"[Net] Sent {msg.MessageCode} ({totalLength + 5} bytes) to session {session.Id}");
                    }
                    else
                    {
                        session.SendSignal.WaitOne(LoopWakeupMs);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stopNetwork.IsCancellationRequested && !session.DisconnectToken.IsCancellationRequested)
                    LogError($"[Net] SenderLoop crash for session {session.Id}: {ex.Message}");
            }
            finally
            {
                SessionTerminate(session);
                try { stream?.Close(); } catch { }
                try { tcpClient.Close(); } catch { }
                LogDebug($"[Net] SenderLoop ended for session {session.Id}");
            }
        }

        void ReceiverLoop(ClientSession session)
        {
            var tcpClient = session.TcpClient;
            if (tcpClient == null) return;

            Stream stream = null;
            try
            {
                stream = tcpClient.GetStream();
            }
            catch
            {
                return;
            }

            LogDebug($"[Net] ReceiverLoop started for session {session.Id}");

            try
            {
                var buffer = new MemoryStream(SendBufferSize);

                while (!_stopNetwork.IsCancellationRequested && !session.DisconnectToken.IsCancellationRequested)
                {
                    ClearStream(buffer);
                    buffer.SetLength(5);

                    int read = ReadFully(stream, buffer.GetBuffer(), 0, 5);
                    if (read != 5)
                        throw new IOException($"[Net] ReceiverLoop expected 5 header bytes but got {read}");

                    buffer.Position = 0;
                    var reader = new BinaryReader(buffer, Encoding.UTF8);
                    int totalLength = reader.ReadInt32();
                    byte codeLen = reader.ReadByte();

                    if (buffer.Capacity < 5 + totalLength)
                        buffer.Capacity = 5 + totalLength;

                    buffer.SetLength(5 + totalLength);
                    read = ReadFully(stream, buffer.GetBuffer(), 5, totalLength);
                    if (read != totalLength)
                        throw new IOException($"[Net] ReceiverLoop expected {totalLength} bytes but got {read}");

                    buffer.Position = 5;
                    var codeBytes = buffer.GetBuffer();
                    string messageCode = Encoding.UTF8.GetString(codeBytes, 5, codeLen);
                    buffer.Position = 5 + codeLen;

                    LogDebug($"[Net] Received message \"{messageCode}\" from session {session.Id}");

                    if (MessageRegistry.TryGetHandler(messageCode, out var prototype))
                    {
                        try
                        {
                            reader = new BinaryReader(buffer, Encoding.UTF8);
                            if (prototype.TryDecode(reader, out var decoded))
                            {
                                decoded.SenderClientId = session.Id;
                                PacketStats.RecordReceived(totalLength + 5);
                                _receiverQueue.Enqueue(decoded);

                                bool firstTime = _loggedFirstReceive.Add(session.Id);
                                if (firstTime)
                                    Log($"[Net] First message received from session {session.Id}: \"{messageCode}\" ({totalLength + 5} bytes)");
                                else
                                    LogDebug($"[Net] Received message \"{messageCode}\" from session {session.Id}");
                            }
                            else
                            {
                                PacketStats.RecordError();
                                LogWarning($"[Net] Failed to decode message \"{messageCode}\" ({totalLength + 5} bytes)");
                            }
                        }
                        catch (Exception ex)
                        {
                            PacketStats.RecordError();
                            LogError($"[Net] Decoder crash for \"{messageCode}\": {ex.Message}");
                        }
                    }
                    else
                    {
                        LogWarning($"[Net] Unknown message type: \"{messageCode}\" ({totalLength + 5} bytes)");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stopNetwork.IsCancellationRequested && !session.DisconnectToken.IsCancellationRequested)
                    LogError($"[Net] ReceiverLoop crash for session {session.Id}: {ex.Message}");
            }
            finally
            {
                SessionTerminate(session);
                try { stream?.Close(); } catch { }
                try { tcpClient.Close(); } catch { }
                LogDebug($"[Net] ReceiverLoop ended for session {session.Id}");
            }
        }

        static int ReadFully(Stream stream, byte[] buffer, int offset, int length)
        {
            int remaining = length;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, offset, remaining);
                if (read <= 0) break;
                offset += read;
                remaining -= read;
            }
            return length - remaining;
        }

        static void ClearStream(MemoryStream ms)
        {
            ms.Position = 0;
            ms.SetLength(0);
        }

        void SessionTerminate(ClientSession session)
        {
            if (session.DisconnectToken.IsCancellationRequested) return;
            session.DisconnectToken.Cancel();

            if (!IsHost && session.Id == 0)
            {
                StatusChanged("Disconnected from host");
                Log("[Net] Disconnected from host.");

                if (ReconnectionManager.IsEnabled && MultiplayerSession.IsActive)
                {
                    ReconnectionManager.StartClientReconnect(
                        MultiplayerSession.LocalPlayerId,
                        session.ClientName ?? "LocalClient",
                        MultiplayerSession.Tick,
                        0);
                }
            }
            else if (IsHost)
            {
                lock (_sessions)
                {
                    _sessions.Remove(session.Id);
                }

                if (ReconnectionManager.IsEnabled && MultiplayerSession.IsActive)
                {
                    string clientName = session.ClientName ?? $"Client_{session.Id}";
                    ReconnectionManager.SaveClientState(
                        session.Id,
                        clientName,
                        MultiplayerSession.Tick,
                        0);
                }

                if (MultiplayerSession.IsActive)
                {
                    MainThreadQueue.Enqueue(() =>
                    {
                        MultiplayerSession.OnPlayerLeft(session.Id);
                    });
                }

                StatusChanged($"Client disconnected: session {session.Id}. Remaining: {_sessions.Count}");
                Log($"[Net] Client session {session.Id} terminated. Remaining clients: {_sessions.Count}");
            }
        }

        public void SendToHost(MessageBase message)
        {
            if (_hostSession != null)
                _hostSession.Send(message);
        }

        public void SendToClient(int clientId, MessageBase message)
        {
            lock (_sessions)
            {
                if (_sessions.TryGetValue(clientId, out var session))
                    session.Send(message);
            }
        }

        public void SendToAllClients(MessageBase message)
        {
            lock (_sessions)
            {
                foreach (var sess in _sessions.Values)
                    sess.Send(message);
            }
        }

        public void SendToAllExcept(int exceptId, MessageBase message)
        {
            lock (_sessions)
            {
                foreach (var sess in _sessions.Values)
                {
                    if (sess.Id != exceptId)
                        sess.Send(message);
                }
            }
        }

        public int ClientCount
        {
            get
            {
                lock (_sessions) return _sessions.Count;
            }
        }

        public void ProcessIncomingMessages()
        {
            while (_receiverQueue.TryDequeue(out var msg))
            {
                if (!ErrorRecoveryManager.ShouldProcessMessage())
                {
                    LogDebug($"[Net] Circuit breaker OPEN — dropping {msg.MessageCode} from client {msg.SenderClientId}");
                    continue;
                }

                if (!RateLimiter.AllowMessage(msg.SenderClientId))
                {
                    ErrorRecoveryManager.RecordError(msg.SenderClientId, "RateLimitExceeded");
                    LogWarning($"[Net] Rate-limited message {msg.MessageCode} from client {msg.SenderClientId} dropped");
                    continue;
                }

                if (!ErrorRecoveryManager.ValidateMessage(msg))
                {
                    ErrorRecoveryManager.RecordError(msg.SenderClientId, "InvalidMessage");
                    LogWarning($"[Net] Invalid message {msg.MessageCode} from client {msg.SenderClientId} dropped");
                    continue;
                }

                MainThreadQueue.Enqueue(() =>
                {
                    if (msg.OnReceive != null)
                        msg.OnReceive(msg);
                    OnMessageReceived?.Invoke(null, msg);
                    HandleMessage(msg);
                });
            }
        }

        void HandleMessage(MessageBase msg)
        {
            switch (msg)
            {
                case MsgConnect connect:
                    string safeName = MessageAuthenticator.SanitizePlayerName(connect.ClientName);
                    Log($"[Net] Client \"{safeName}\" connected (session {msg.SenderClientId})");
                    MultiplayerSession.OnClientConnected(msg.SenderClientId, safeName);

                    if (IsHost)
                    {
                        var hostJoin = new MsgPlayerJoin
                        {
                            PlayerId = 0,
                            PlayerName = "Host"
                        };
                        SendToClient(msg.SenderClientId, hostJoin);

                        var ids = MultiplayerSession.GetPlayerIds();
                        var names = new string[ids.Length];
                        for (int i = 0; i < ids.Length; i++)
                            MultiplayerSession.TryGetPlayerName(ids[i], out names[i]);

                        var roster = new MsgRosterSync
                        {
                            PlayerIds = ids,
                            PlayerNames = names
                        };
                        SendToClient(msg.SenderClientId, roster);

                        // Send host's own character data to the new client
                        var hostCharData = Session.RemoteCharacterManager.BuildLocalCharacterDataSync();
                        if (hostCharData != null)
                        {
                            SendToClient(msg.SenderClientId, hostCharData);
                            Log($"[Paramulti][Network] Sent host character data to new client {msg.SenderClientId}");
                        }
                    }
                    break;

                case MsgDisconnect disconnect:
                    Log($"[Net] Disconnect received: {disconnect.Reason}");
                    break;

                case MsgPlayerJoin join:
                    Log($"[Paramulti][Network] Received MsgPlayerJoin for Player {join.PlayerId} (Name={join.PlayerName}).");
                    MultiplayerSession.OnPlayerJoined(join.PlayerId, join.PlayerName);

                    if (IsHost)
                    {
                        // Rebroadcast to all other clients so everyone knows about the new player
                        SendToAllExcept(msg.SenderClientId, join);
                        // Send acknowledgment back to the joining client so it triggers character data sync
                        SendToClient(msg.SenderClientId, join);
                    }

                    // Client sends its own character data upon receiving join acknowledgment
                    if (!IsHost && join.PlayerId == MultiplayerSession.LocalPlayerId)
                    {
                        var myData = Session.RemoteCharacterManager.BuildLocalCharacterDataSync();
                        if (myData != null)
                        {
                            SendToHost(myData);
                            Log($"[Paramulti][Network] Sent local character data to host");
                        }
                    }
                    break;

                case MsgPlayerLeave leave:
                    Log($"[Net] Player left: ID={leave.PlayerId}");
                    MultiplayerSession.OnPlayerLeft(leave.PlayerId);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, msg);
                    break;

                case MsgUpdateState update:
                    Session.PlayerSyncManager.EnqueueState(update);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, update);
                    break;

                case MsgChat chat:
                    chat.Message = MessageAuthenticator.SanitizeChatMessage(chat.Message);
                    if (MessageAuthenticator.ContainsInjection(chat.Message))
                    {
                        ErrorRecoveryManager.RecordError(msg.SenderClientId, "InjectionAttempt");
                        LogWarning($"[Net] Chat injection attempt from client {msg.SenderClientId} blocked");
                        return;
                    }
                    ChatManager.HandleChat(chat);
                    break;

                case MsgCursorPing ping:
                    LogDebug($"[Net] CursorPing from player {ping.PlayerId} at {ping.Position}");
                    Session.BuildCursorSyncManager.ReceiveCursorPing(ping);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, ping);
                    break;

                case MsgAnimationState anim:
                    LogDebug($"[Net] AnimationState from player {anim.PlayerId}: hash={anim.AnimatorStateHash}, time={anim.NormalizedTime:F2}");
                    Session.AnimationSyncManager.ReceiveAnimationState(anim);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, anim);
                    break;

                case MsgBuildObjectPlaced build:
                    LogDebug($"[Net] BuildObjectPlaced: type={build.ObjectTypeId}, pos={build.Position}, seq={build.SequenceNumber}");
                    if (IsHost)
                    {
                        LogDebug("[Net] Host rebroadcasting BuildObjectPlaced to all clients.");
                        SendToAllExcept(msg.SenderClientId, build);
                    }
                    break;

                case MsgSyncState sync:
                    LogDebug($"[Net] SyncState received: tick={sync.Tick}, players={sync.PlayerCount}");
                    break;

                case MsgEntitySpawn spawn:
                    LogDebug($"[Net] EntitySpawn: id={spawn.EntityId}, type={spawn.EntityType}, pos={spawn.Position}");
                    Session.EntitySyncManager.ApplyRemoteSpawn(spawn);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, spawn);
                    break;

                case MsgEntityDespawn despawn:
                    LogDebug($"[Net] EntityDespawn: id={despawn.EntityId}");
                    Session.EntitySyncManager.ApplyRemoteDespawn(despawn);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, despawn);
                    break;

                case MsgRequestFullState reqState:
                    LogDebug($"[Net] RequestFullState from player {reqState.PlayerId}");
                    if (IsHost)
                    {
                        var snapshot = Session.EntitySyncManager.BuildSnapshot();
                        SendToClient(msg.SenderClientId, snapshot);
                    }
                    break;

                case MsgFullStateSnapshot snap:
                    LogDebug($"[Net] FullStateSnapshot: tick={snap.Tick}, entities={snap.Entities.Count}");
                    Session.EntitySyncManager.ApplyFullStateSnapshot(snap);
                    break;

                case MsgReadyCheck ready:
                    LogDebug($"[Net] ReadyCheck: player {ready.PlayerId} is {(ready.IsReady ? "READY" : "NOT READY")}");
                    Session.LobbyManager.HandleRemoteReady(ready);
                    break;

                case MsgInputCommand inputCmd:
                    LogDebug($"[Net] InputCommand from player {inputCmd.PlayerId}: action={inputCmd.Action}");
                    InputRouter.ProcessRemoteInput(inputCmd);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, inputCmd);
                    break;

                case MsgBuildModeEvent buildEvt:
                    LogDebug($"[Net] BuildModeEvent: type={buildEvt.EventType}, entity={buildEvt.EntityId}, player={buildEvt.PlayerId}");
                    if (IsHost)
                    {
                        BuildSyncManager.ValidateAndApply(buildEvt);
                        LogDebug("[Net] Host rebroadcasting BuildModeEvent to all clients.");
                        SendToAllExcept(msg.SenderClientId, buildEvt);
                    }
                    else
                    {
                        BuildSyncManager.ValidateAndApply(buildEvt);
                    }
                    break;

                case MsgHeartbeat hb:
                    Log($"[Net] HandleMessage MsgHeartbeat: sender={msg.SenderClientId}, playerId={hb.PlayerId}, tick={hb.Tick}");
                    if (IsHost)
                    {
                        DesyncDetector.ProcessHeartbeat(hb);
                        SendToAllExcept(msg.SenderClientId, hb);
                    }
                    break;

                case MsgSaveInitiate saveInit:
                    LogDebug($"[Net] SaveInitiate: tick={saveInit.Tick}, scene={saveInit.SceneName}");
                    if (!IsHost)
                    {
                        var ack = new MsgSaveAck
                        {
                            PlayerId = MultiplayerSession.LocalPlayerId,
                            Tick = saveInit.Tick,
                            Success = true,
                            ErrorMessage = ""
                        };
                        SendToHost(ack);
                    }
                    break;

                case MsgSaveAck saveAck:
                    LogDebug($"[Net] SaveAck: player={saveAck.PlayerId}, success={saveAck.Success}");
                    if (IsHost)
                        SaveManager.HandleSaveAck(saveAck);
                    break;

                case MsgSaveComplete saveComp:
                    LogDebug($"[Net] SaveComplete: acks={saveComp.AcksReceived}/{saveComp.TotalPlayers}");
                    SaveManager.HandleSaveComplete(saveComp);
                    break;

                case MsgLoadInitiate loadInit:
                    LogDebug($"[Net] LoadInitiate: tick={loadInit.Tick}, scene={loadInit.SceneName}, chunks={loadInit.TotalChunks}");
                    SaveManager.HandleLoadInitiate(loadInit);
                    break;

                case MsgLoadStateChunk loadChunk:
                    LogDebug($"[Net] LoadStateChunk: {loadChunk.ChunkIndex}/{loadChunk.TotalChunks}, entities={loadChunk.Entities.Count}");
                    SaveManager.HandleLoadChunk(loadChunk);
                    break;

                case MsgLoadComplete loadComp:
                    LogDebug($"[Net] LoadComplete: player={loadComp.PlayerId}, chunks={loadComp.ChunksReceived}/{loadComp.TotalChunks}");
                    if (IsHost)
                        SaveManager.HandleLoadComplete(loadComp);
                    else
                    {
                        var ack = new MsgLoadComplete
                        {
                            PlayerId = MultiplayerSession.LocalPlayerId,
                            Tick = MultiplayerSession.Tick,
                            Success = true,
                            ChunksReceived = loadComp.ChunksReceived,
                            TotalChunks = loadComp.TotalChunks
                        };
                        SendToHost(ack);
                    }
                    break;

                case MsgPing ping:
                    LogDebug($"[Net] Ping from player {ping.PlayerId}");
                    ConnectionQualityMonitor.RecordPingSent(ping.PlayerId);
                    var pong = new MsgPong
                    {
                        PlayerId = ping.PlayerId,
                        OriginalTimestampMs = ping.TimestampMs,
                        ReplyTimestampMs = System.Diagnostics.Stopwatch.GetTimestamp()
                    };
                    if (IsHost)
                        SendToClient(msg.SenderClientId, pong);
                    else
                        SendToHost(pong);
                    break;

                case MsgPong pongMsg:
                    long rtt = (System.Diagnostics.Stopwatch.GetTimestamp() - pongMsg.OriginalTimestampMs) * 1000 / (long)System.Diagnostics.Stopwatch.Frequency;
                    LogDebug($"[Net] Pong from player {pongMsg.PlayerId}, RTT={rtt}ms");
                    ConnectionQualityMonitor.RecordPongReceived(pongMsg.PlayerId, rtt);
                    break;

                case MsgReconnectRequest reconnReq:
                    LogDebug($"[Net] ReconnectRequest from player {reconnReq.PlayerId}, lastTick={reconnReq.LastKnownTick}");
                    if (IsHost)
                    {
                        var allowed = ReconnectionManager.ValidateReconnectRequest(reconnReq, out var reconnAck);
                        SendToClient(msg.SenderClientId, reconnAck);
                        if (allowed)
                        {
                            Log($"[Net] Client {reconnReq.PlayerId} reconnected successfully");
                            MultiplayerSession.OnPlayerJoined(reconnReq.PlayerId, reconnReq.ClientName);
                            var snapshot = Session.EntitySyncManager.BuildSnapshot();
                            SendToClient(msg.SenderClientId, snapshot);
                        }
                    }
                    break;

                case MsgReconnectAck reconnAck:
                    LogDebug($"[Net] ReconnectAck for player {reconnAck.PlayerId}, allowed={reconnAck.Allowed}");
                    ReconnectionManager.OnReconnectAckReceived(reconnAck);
                    if (reconnAck.Allowed)
                    {
                        MultiplayerSession.OnPlayerJoined(reconnAck.PlayerId,
                            MultiplayerSession.TryGetPlayerName(reconnAck.PlayerId, out var rname) ? rname : "Reconnected");
                    }
                    break;

                case MsgHostMigration migration:
                    Log($"[Net] HostMigration: newHost={migration.NewHostAddress}:{migration.NewHostPort}, playerId={migration.NewHostPlayerId}");
                    if (!IsHost)
                    {
                        Log("[Net] Migrating to new host...");
                        StatusChanged("Migrating to new host: " + migration.NewHostAddress);
                        System.Threading.Tasks.Task.Run(() =>
                        {
                            Shutdown();
                            System.Threading.Thread.Sleep(500);
                            StartClient(migration.NewHostAddress, migration.NewHostPort);
                        });
                    }
                    break;

                case MsgRosterSync roster:
                    Log($"[Paramulti][Network] RosterSync received with {roster.PlayerIds?.Length ?? 0} players.");
                    if (roster.PlayerIds != null)
                    {
                        for (int i = 0; i < roster.PlayerIds.Length; i++)
                        {
                            var pid = roster.PlayerIds[i];
                            var pname = roster.PlayerNames != null && i < roster.PlayerNames.Length ? roster.PlayerNames[i] : $"Player_{pid}";
                            if (pid != MultiplayerSession.LocalPlayerId)
                            {
                                MultiplayerSession.OnPlayerJoined(pid, pname);
                            }
                        }
                    }
                    break;

                case MsgCharacterDataSync charData:
                    Log($"[Paramulti][Network] CharacterDataSync from player {charData.PlayerId}: GUID={charData.CharacterGuid:X}");
                    Session.RemoteCharacterManager.ApplyRemoteCharacterDataSync(charData);
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, charData);
                    break;

                case MsgInteractionRequest interactReq:
                    Log($"[Paramulti][Network] InteractionRequest from player {interactReq.RequesterPlayerId}: target={interactReq.TargetCharacterGuid:X}, interaction={interactReq.InteractionGuid:X}");
                    Session.InteractionSyncManager.ProcessRemoteInteractionRequest(interactReq);
                    break;

                case MsgSelectCharacter selectChar:
                    Log($"[Paramulti][Network] SelectCharacter from player {selectChar.PlayerId}: GUID={selectChar.CharacterGuid:X}, selected={selectChar.Selected}");
                    if (IsHost)
                        SendToAllExcept(msg.SenderClientId, selectChar);
                    break;
            }
        }

        void StatusChanged(string msg)
        {
            OnStatusChanged?.Invoke(msg);
            Log($"[Net] {msg}");
        }

        public void Shutdown()
        {
            if (_stopNetwork == null) return;

            Log("[Net] Shutting down...");

            // Stop the listener first to unblock AcceptTcpClient() and release the port immediately
            try { _listener?.Stop(); } catch { }

            _stopHostAcceptor?.Cancel();
            _stopNetwork.Cancel();

            lock (_sessions)
            {
                foreach (var sess in _sessions.Values)
                {
                    try { sess.TcpClient?.Close(); } catch { }
                }
                _sessions.Clear();
            }

            if (_hostSession != null)
            {
                try { _hostSession.TcpClient?.Close(); } catch { }
                _hostSession = null;
            }

            IsHost = false;
            _stopNetwork = null;
            _stopHostAcceptor = null;
            _listener = null;
            Log("[Net] Shutdown complete.");
        }

        public void Dispose()
        {
            Shutdown();
            Instance = null;
        }

        static void Log(object msg) => Plugin.Log?.LogInfo(msg.ToString() ?? "");
        static void LogDebug(object msg)
        {
            if (Plugin.VerboseLogging)
                Plugin.Log?.LogInfo($"[Debug] {msg}");
        }
        static void LogError(object msg) => Plugin.Log?.LogError(msg.ToString() ?? "");
        static void LogWarning(object msg) => Plugin.Log?.LogWarning(msg.ToString() ?? "");
    }
}
