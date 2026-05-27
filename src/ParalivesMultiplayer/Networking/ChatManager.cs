using System;
using System.Collections.Generic;
using ParalivesMultiplayer.Networking.Messages;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Networking
{
    public static class ChatManager
    {
        const int MaxHistory = 64;
        static readonly Queue<string> _history = new Queue<string>(MaxHistory);
        static readonly object _lock = new object();

        public static event Action<string> OnChatMessageReceived;

        public static void SendChat(string message)
        {
            var net = TcpNetworkManager.Instance;
            if (net == null) return;

            var chatMsg = new MsgChat
            {
                PlayerName = "LocalPlayer",
                Message = message
            };

            if (MultiplayerSession.IsHost)
            {
                AddToHistory("You: " + message);
                net.SendToAllClients(chatMsg);
                Plugin.Log.LogInfo($"[Chat] Host broadcast: {message}");
            }
            else
            {
                AddToHistory("You: " + message);
                net.SendToHost(chatMsg);
                Plugin.Log.LogInfo($"[Chat] Sent to host: {message}");
            }
        }

        public static void HandleChat(MsgChat msg)
        {
            var display = $"{msg.PlayerName}: {msg.Message}";
            AddToHistory(display);
            OnChatMessageReceived?.Invoke(display);
            Plugin.Log.LogInfo($"[Chat] {display}");

            if (MultiplayerSession.IsHost && TcpNetworkManager.Instance != null)
            {
                TcpNetworkManager.Instance.SendToAllExcept(msg.SenderClientId, msg);
            }
        }

        static void AddToHistory(string entry)
        {
            lock (_lock)
            {
                while (_history.Count >= MaxHistory)
                    _history.Dequeue();
                _history.Enqueue(entry);
            }
        }

        public static string[] GetHistory()
        {
            lock (_lock) return _history.ToArray();
        }

        public static void ClearHistory()
        {
            lock (_lock) _history.Clear();
        }
    }
}
