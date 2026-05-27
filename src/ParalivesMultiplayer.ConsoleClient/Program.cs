using System;
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
        static int _myId;

        static int Main(string[] args)
        {
            string host = "127.0.0.1";
            int port = 7890;

            if (args.Length >= 1) host = args[0];
            if (args.Length >= 2) int.TryParse(args[1], out port);

            Console.WriteLine($"Connecting to {host}:{port} ...");

            try
            {
                _socket = new TcpClient();
                _socket.Connect(host, port);
                _stream = _socket.GetStream();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect: {ex.Message}");
                return 1;
            }

            MessageRegistry.LogAction = msg => Console.WriteLine($"[REG] {msg}");
            MessageRegistry.RegisterAll();

            var connectMsg = new MsgConnect { ClientName = "ConsoleClient" };
            Send(connectMsg);
            Console.WriteLine("Sent Connect, waiting for server...");
            Console.WriteLine("Type 'quit' to exit. Type 'help' for commands.");

            _running = true;
            var recvThread = new Thread(ReceiveLoop) { IsBackground = true };
            recvThread.Start();

            var inputThread = new Thread(InputLoop) { IsBackground = true };
            inputThread.Start();

            inputThread.Join();
            _socket?.Close();
            return 0;
        }

        static void Send(MessageBase msg)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms, Encoding.UTF8);

            var codeBytes = Encoding.UTF8.GetBytes(msg.MessageCode);
            int payloadLen = codeBytes.Length + (int)ms.Length;

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

            _stream.Write(full.GetBuffer(), 0, (int)full.Length);
            _stream.Flush();
            Console.WriteLine($">> {msg.MessageCode}");
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
                            Console.WriteLine($"<< [{decoded.SenderClientId}] {decoded.MessageCode}: {FormatMessage(decoded)}");
                            if (decoded is MsgConnect c && c.ClientName != null)
                                _myId = decoded.SenderClientId;
                        }
                        else
                        {
                            Console.WriteLine($"<< Failed to decode \"{messageCode}\"");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"<< Unknown message type: \"{messageCode}\"");
                    }
                }
                catch (Exception ex)
                {
                    if (_running) Console.WriteLine($"[RX] Error: {ex.Message}");
                    break;
                }
            }
        }

        static int ReadFully(NetworkStream stream, byte[] buffer, int offset, int length)
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

        static void InputLoop()
        {
            while (_running)
            {
                string line = Console.ReadLine();
                if (string.IsNullOrEmpty(line)) continue;

                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                string cmd = parts[0].ToLower();
                string arg = parts.Length > 1 ? parts[1] : "";

                switch (cmd)
                {
                    case "quit":
                    case "exit":
                        Send(new MsgDisconnect { Reason = "Client quit" });
                        _running = false;
                        return;
                    case "help":
                        Console.WriteLine("Commands: quit, help, chat <msg>, ping, heartbeat");
                        break;
                    case "chat":
                        Send(new MsgChat { PlayerName = "ConsoleClient", Message = arg });
                        break;
                    case "ping":
                        Send(new MsgPing { PlayerId = _myId, TimestampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
                        break;
                    case "heartbeat":
                        Send(new MsgHeartbeat { PlayerId = _myId, Tick = 0, SequenceNumber = 0, TimestampMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() });
                        break;
                    default:
                        Console.WriteLine($"Unknown command: {cmd}. Type 'help' for commands.");
                        break;
                }
            }
        }

        static string FormatMessage(MessageBase msg)
        {
            switch (msg)
            {
                case MsgConnect m: return $"name={m.ClientName}";
                case MsgDisconnect m: return $"reason={m.Reason}";
                case MsgChat m: return $"{m.PlayerName}: {m.Message}";
                case MsgPing m: return $"ts={m.TimestampMs}";
                case MsgPong m: return $"ts={m.TimestampMs}";
                case MsgHeartbeat m: return $"tick={m.Tick} seq={m.SequenceNumber} ts={m.TimestampMs}";
                case MsgSyncState m: return $"tick={m.Tick} players={m.PlayerCount}";
                case MsgPlayerJoin m: return $"playerId={m.PlayerId} name={m.PlayerName}";
                case MsgPlayerLeave m: return $"playerId={m.PlayerId}";
                default: return msg.MessageCode;
            }
        }
    }
}
