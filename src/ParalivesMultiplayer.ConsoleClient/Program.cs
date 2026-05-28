using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.ConsoleClient
{
    class Program
    {
        static TcpClient _socket;
        static NetworkStream _stream;
        static bool _running;
        static int _myId = -1;
        static float _posX, _posY, _posZ;
        static uint _tick;
        static bool _loopRunning;

        static readonly object _consoleLock = new object();
        static readonly Queue<string> _messageQueue = new Queue<string>();

        static int Main(string[] args)
        {
            string host = "127.0.0.1";
            int port = 7890;

            if (args.Length >= 1) host = args[0];
            if (args.Length >= 2) int.TryParse(args[1], out port);

            SafeWriteLine($"Connecting to {host}:{port} ...");

            try
            {
                _socket = new TcpClient();
                _socket.Connect(host, port);
                _stream = _socket.GetStream();
            }
            catch (Exception ex)
            {
                SafeWriteLine($"Failed to connect: {ex.Message}");
                return 1;
            }

            MessageRegistry.LogAction = msg => EnqueueMsg($"[REG] {msg}");
            MessageRegistry.RegisterAll();

            var connectMsg = new MsgConnect { ClientName = "ConsoleClient" };
            Send(connectMsg);
            EnqueueMsg("Sent Connect, waiting for server...");
            EnqueueMsg("Type 'quit' to exit. Type 'help' for commands.");

            _running = true;
            var recvThread = new Thread(ReceiveLoop) { IsBackground = true };
            recvThread.Start();

            InputLoop();
            _loopRunning = false;
            _socket?.Close();
            return 0;
        }

        static void EnqueueMsg(string msg)
        {
            lock (_messageQueue)
                _messageQueue.Enqueue(msg);
        }

        static void FlushMessages()
        {
            lock (_consoleLock)
            {
                lock (_messageQueue)
                {
                    while (_messageQueue.Count > 0)
                        Console.WriteLine(_messageQueue.Dequeue());
                }
            }
        }

        static void SafeWriteLine(string msg)
        {
            lock (_consoleLock)
                Console.WriteLine(msg);
        }

        static void Send(MessageBase msg)
        {
            var ms = new MemoryStream();

            var codeBytes = Encoding.UTF8.GetBytes(msg.MessageCode);

            var full = new MemoryStream();
            var fw = new BinaryWriter(full, Encoding.UTF8);

            var tempMs = new MemoryStream();
            var tempW = new BinaryWriter(tempMs, Encoding.UTF8);
            tempW.Write(codeBytes);
            msg.Encode(tempW);
            tempW.Flush();

            byte[] payload = tempMs.ToArray();
            fw.Write(payload.Length);
            fw.Write((byte)codeBytes.Length);
            fw.Write(payload);
            fw.Flush();

            lock (_stream)
            {
                _stream.Write(full.GetBuffer(), 0, (int)full.Length);
                _stream.Flush();
            }
            EnqueueMsg($">> {msg.MessageCode}");
        }

        static void ReceiveLoop()
        {
            var buffer = new byte[65536];
            while (_running && _socket?.Connected == true)
            {
                try
                {
                    int headerRead = ReadFully(_stream, buffer, 0, 5);
                    if (headerRead != 5) break;

                    int totalLength = BitConverter.ToInt32(buffer, 0);
                    byte codeLen = buffer[4];

                    int bodyRead = ReadFully(_stream, buffer, 5, totalLength);
                    if (bodyRead != totalLength) break;

                    string messageCode = Encoding.UTF8.GetString(buffer, 5, codeLen);

                    var ms = new MemoryStream(buffer, 5 + codeLen, totalLength - codeLen);
                    var reader = new BinaryReader(ms, Encoding.UTF8);

                    if (MessageRegistry.TryGetHandler(messageCode, out var prototype))
                    {
                        if (prototype.TryDecode(reader, out var decoded))
                        {
                            EnqueueMsg($"<< [{decoded.SenderClientId}] {decoded.MessageCode}: {FormatMessage(decoded)}");
                            if (decoded is MsgConnect c && c.ClientName != null)
                                _myId = decoded.SenderClientId;
                        }
                        else
                        {
                            EnqueueMsg($"<< Failed to decode \"{messageCode}\"");
                        }
                    }
                    else
                    {
                        EnqueueMsg($"<< Unknown message type: \"{messageCode}\"");
                    }
                }
                catch (Exception ex)
                {
                    if (_running) EnqueueMsg($"[RX] Error: {ex.Message}");
                    break;
                }
            }
        }

        static int ReadFully(NetworkStream stream, byte[] buffer, int offset, int length)
        {
            lock (_stream)
            {
                int remaining = length;
                while (remaining > 0)
                {
                    int read = stream.Read(buffer, offset + (length - remaining), remaining);
                    if (read <= 0) break;
                    remaining -= read;
                }
                return length - remaining;
            }
        }

        static void InputLoop()
        {
            while (_running)
            {
                FlushMessages();
                lock (_consoleLock)
                {
                    Console.Write("> ");
                    string line = Console.ReadLine();
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    string cmd = parts[0].ToLower();
                    string arg = parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : "";

                    switch (cmd)
                    {
                        case "quit":
                        case "exit":
                            Send(new MsgDisconnect { Reason = "Client quit" });
                            _running = false;
                            return;
                        case "help":
                            EnqueueMsg("Commands:");
                            EnqueueMsg("  quit              - disconnect");
                            EnqueueMsg("  help              - show this help");
                            EnqueueMsg("  chat <msg>        - send chat message");
                            EnqueueMsg("  ping              - send ping");
                            EnqueueMsg("  heartbeat         - send heartbeat");
                            EnqueueMsg("  join <name>       - send explicit PlayerJoin");
                            EnqueueMsg("  move x y z        - set position and send UpdateState");
                            EnqueueMsg("  nudge dx dy dz    - add offset to position and send UpdateState");
                            EnqueueMsg("  loop [interval_ms]- auto-send state updates in a circle (default 200ms)");
                            EnqueueMsg("  stop              - stop the movement loop");
                            EnqueueMsg("  pos               - show current position");
                            break;
                        case "chat":
                            Send(new MsgChat { PlayerName = "ConsoleClient", Message = arg });
                            break;
                        case "ping":
                            Send(new MsgPing { PlayerId = _myId, TimestampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
                            break;
                        case "heartbeat":
                            Send(new MsgHeartbeat { PlayerId = _myId, Tick = _tick, SequenceNumber = 0, TimestampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
                            break;
                        case "join":
                            {
                                string name = string.IsNullOrEmpty(arg) ? "ConsoleClient" : arg;
                                Send(new MsgPlayerJoin { PlayerId = _myId >= 0 ? _myId : 1, PlayerName = name });
                                EnqueueMsg($"Sent PlayerJoin id={_myId} name={name}");
                            }
                            break;
                        case "move":
                            {
                                var nums = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (nums.Length >= 3)
                                {
                                    _posX = float.Parse(nums[0]);
                                    _posY = float.Parse(nums[1]);
                                    _posZ = float.Parse(nums[2]);
                                }
                                SendUpdateState();
                            }
                            break;
                        case "nudge":
                            {
                                var nums = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (nums.Length >= 3)
                                {
                                    _posX += float.Parse(nums[0]);
                                    _posY += float.Parse(nums[1]);
                                    _posZ += float.Parse(nums[2]);
                                }
                                SendUpdateState();
                            }
                            break;
                        case "loop":
                            {
                                int interval = 200;
                                if (!string.IsNullOrEmpty(arg)) int.TryParse(arg, out interval);
                                if (_loopRunning)
                                {
                                    EnqueueMsg("Loop already running. Use 'stop' first.");
                                }
                                else
                                {
                                    _loopRunning = true;
                                    var t = new Thread(() => MovementLoop(interval)) { IsBackground = true };
                                    t.Start();
                                    EnqueueMsg($"Movement loop started ({interval}ms interval)");
                                }
                            }
                            break;
                        case "stop":
                            _loopRunning = false;
                            EnqueueMsg("Movement loop stopped.");
                            break;
                        case "pos":
                            EnqueueMsg($"Position:({_posX}, {_posY}, {_posZ})  Tick:{_tick}  PlayerId:{_myId}");
                            break;
                        default:
                            EnqueueMsg($"Unknown command: {cmd}. Type 'help' for commands.");
                            break;
                    }
                }
            }
        }

        static void MovementLoop(int intervalMs)
        {
            double angle = 0;
            double radius = 5.0;
            while (_loopRunning && _running)
            {
                _posX = (float)(radius * Math.Cos(angle));
                _posZ = (float)(radius * Math.Sin(angle));
                _posY = 0f;
                SendUpdateState();
                angle += 0.15;
                Thread.Sleep(intervalMs);
            }
        }

        static void SendUpdateState()
        {
            var msg = new MsgUpdateState
            {
                Tick = _tick++,
                PlayerId = _myId >= 0 ? _myId : 1,
                Position = new NetVector3(_posX, _posY, _posZ),
                Velocity = NetVector3.zero,
                Rotation = NetQuaternion.identity,
                InputHorizontal = 0f,
                InputVertical = 0f,
                JumpPressed = false,
                AttackPressed = false
            };
            Send(msg);
            EnqueueMsg($"    pos=({_posX:F1}, {_posY:F1}, {_posZ:F1}) tick={msg.Tick}");
        }

        static string FormatMessage(MessageBase msg)
        {
            switch (msg)
            {
                case MsgConnect m: return $"name={m.ClientName}";
                case MsgDisconnect m: return $"reason={m.Reason}";
                case MsgChat m: return $"{m.PlayerName}: {m.Message}";
                case MsgPing m: return $"ts={m.TimestampMs}";
                case MsgPong m: return $"orig={m.OriginalTimestampMs} reply={m.ReplyTimestampMs}";
                case MsgHeartbeat m: return $"tick={m.Tick} seq={m.SequenceNumber} ts={m.TimestampMs}";
                case MsgSyncState m: return $"tick={m.Tick} players={m.PlayerCount}";
                case MsgPlayerJoin m: return $"playerId={m.PlayerId} name={m.PlayerName}";
                case MsgPlayerLeave m: return $"playerId={m.PlayerId}";
                case MsgUpdateState m: return $"pos=({m.Position.x:F1},{m.Position.y:F1},{m.Position.z:F1}) tick={m.Tick}";
                default: return msg.MessageCode;
            }
        }
    }
}
