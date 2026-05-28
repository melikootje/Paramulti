using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParalivesMultiplayer.UI
{
    public static class MultiplayerHUD
    {
        static readonly GUIStyle _labelStyle = new GUIStyle();
        static readonly GUIStyle _headerStyle = new GUIStyle();
        static readonly GUIStyle _chatStyle = new GUIStyle();
        static readonly GUIStyle _inputStyle = new GUIStyle();
        static readonly GUIStyle _bgStyle = new GUIStyle();
        static bool _initialized;

        static string _statusText = "Not connected";
        static string _debugText = "";
        static bool _showDebug = true;
        static bool _showChat = true;
        static string _chatInput = "";
        static readonly Queue<string> _chatLog = new Queue<string>(32);

        public static void Initialize()
        {
            if (_initialized) return;

            _labelStyle.fontSize = 14;
            _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            _labelStyle.alignment = TextAnchor.UpperLeft;

            _headerStyle.fontSize = 16;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = new Color(0.3f, 0.7f, 1f);
            _headerStyle.alignment = TextAnchor.UpperLeft;

            _chatStyle.fontSize = 13;
            _chatStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _chatStyle.alignment = TextAnchor.UpperLeft;
            _chatStyle.wordWrap = true;

            _inputStyle.fontSize = 13;
            _inputStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            _inputStyle.alignment = TextAnchor.MiddleLeft;
            _inputStyle.border = new RectOffset(4, 4, 4, 4);
            _inputStyle.padding = new RectOffset(6, 6, 3, 3);

            _bgStyle.normal.background = MakeTexture(32, 32, new Color(0f, 0f, 0f, 0.55f));

            var inst = ParalivesMultiplayer.Networking.TcpNetworkManager.Instance;
            if (inst != null)
                inst.OnStatusChanged += OnStatusChanged;

            ParalivesMultiplayer.Networking.ChatManager.OnChatMessageReceived += AddChatEntry;
            _initialized = true;
        }

        static Texture2D MakeTexture(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    tex.SetPixel(x, y, col);
            tex.Apply();
            return tex;
        }

        static void OnStatusChanged(string msg)
        {
            _statusText = msg;
        }

        static void AddChatEntry(string entry)
        {
            lock (_chatLog)
            {
                while (_chatLog.Count >= 32)
                    _chatLog.Dequeue();
                _chatLog.Enqueue(entry);
            }
        }

        public static void Update()
        {
            if (!_initialized) Initialize();

            var net = ParalivesMultiplayer.Networking.TcpNetworkManager.Instance;

            if (net != null && ParalivesMultiplayer.Session.MultiplayerSession.IsActive)
            {
                _debugText = $"Mode: {(net.IsHost ? "HOST" : "CLIENT")} | " +
                    $"Players: {ParalivesMultiplayer.Session.MultiplayerSession.PlayerCount} | " +
                    $"Tick: {ParalivesMultiplayer.Session.MultiplayerSession.Tick} | " +
                    $"Queue: {ParalivesMultiplayer.Networking.MainThreadQueue.PendingCount}";
            }
            else
            {
                _debugText = "Idle";
            }
        }

        public static void Draw()
        {
            if (!_initialized) return;

            var net = ParalivesMultiplayer.Networking.TcpNetworkManager.Instance;
            if (net == null || !ParalivesMultiplayer.Session.MultiplayerSession.IsActive)
                return;

            float x = 10f;
            float y = 10f;

            GUI.Label(new Rect(x, y, 400f, 30f), "Paralives Multiplayer", _headerStyle);
            y += 35f;

            GUI.Label(new Rect(x, y, 500f, 25f), $"Status: {_statusText}", _labelStyle);
            y += 30f;

            GUI.Label(new Rect(x, y, 500f, 25f), _debugText, _labelStyle);
            y += 30f;

            double avgPing = ParalivesMultiplayer.Networking.PacketStats.AveragePingMs;
            int pingSamples = ParalivesMultiplayer.Networking.PacketStats.PingSampleCount;
            string pingStr = pingSamples > 0 ? $"{avgPing:F1} ms ({pingSamples} samples)" : "N/A";
            GUI.Label(new Rect(x, y, 500f, 25f), $"Ping: {pingStr}", _labelStyle);
            y += 30f;

            long bytesSent = ParalivesMultiplayer.Networking.PacketStats.TotalBytesSent;
            long bytesRecv = ParalivesMultiplayer.Networking.PacketStats.TotalBytesReceived;
            int msgSent = ParalivesMultiplayer.Networking.PacketStats.TotalMessagesSent;
            int msgRecv = ParalivesMultiplayer.Networking.PacketStats.TotalMessagesReceived;
            int errors = ParalivesMultiplayer.Networking.PacketStats.TotalErrors;

            GUI.Label(new Rect(x, y, 500f, 25f),
                $"Packets: Sent={msgSent}, Recv={msgRecv} | " +
                $"Bytes: {ParalivesMultiplayer.Networking.PacketStats.FormatBytes(bytesSent)} / {ParalivesMultiplayer.Networking.PacketStats.FormatBytes(bytesRecv)}" +
                (errors > 0 ? $" | Errors: {errors}" : ""), _labelStyle);
            y += 30f;

            if (GUI.Button(new Rect(x, y, 80f, 22f), "Toggle Debug"))
                _showDebug = !_showDebug;
            y += 27f;

if (_showDebug)
             {
                 GUI.Box(new Rect(x, y, 420f, 140f), "", _bgStyle);
                 float dy = y + 5f;
                 GUI.Label(new Rect(x + 5f, dy, 410f, 18f), $"[D] Tick: {ParalivesMultiplayer.Session.MultiplayerSession.Tick}", _labelStyle);
                 dy += 20f;
                 GUI.Label(new Rect(x + 5f, dy, 410f, 18f), $"[D] Queue pending: {ParalivesMultiplayer.Networking.MainThreadQueue.PendingCount}", _labelStyle);
                 dy += 20f;
                 GUI.Label(new Rect(x + 5f, dy, 410f, 18f), $"[D] Sessions: {net.ClientCount}", _labelStyle);
                 dy += 20f;
                 GUI.Label(new Rect(x + 5f, dy, 410f, 18f), $"[D] Chat entries: {_chatLog.Count}", _labelStyle);
                 dy += 20f;
                 GUI.Label(new Rect(x + 5f, dy, 410f, 18f), $"[D] Remote Players: {ParalivesMultiplayer.Session.PlayerSyncManager.RemotePlayerCount}", _labelStyle);
                 dy += 20f;
if (GUI.Button(new Rect(x + 5f, dy, 100f, 20f), ParalivesMultiplayer.Plugin.EnableLivePlayerSync ? "Live Sync: ON" : "Live Sync: OFF"))
                  {
                      ParalivesMultiplayer.Plugin.EnableLivePlayerSync = !ParalivesMultiplayer.Plugin.EnableLivePlayerSync;
                  }
                 dy += 22f;
                 GUI.Label(new Rect(x + 5f, dy, 410f, 18f), $"[D] F5=Host | F6=Client | F7=Disconnect", _labelStyle);
             }

            float chatX = x + 440f;
            float chatY = 10f;

            if (GUI.Button(new Rect(chatX, chatY, 80f, 22f), "Toggle Chat"))
                _showChat = !_showChat;

            if (_showChat)
            {
                GUI.Box(new Rect(chatX, chatY + 27f, 340f, 200f), "", _bgStyle);
                float logY = chatY + 32f;
                float maxLogH = 150f;

                lock (_chatLog)
                {
                    var arr = _chatLog.ToArray();
                    for (int i = arr.Length - 1; i >= 0; i--)
                    {
                        if (logY > chatY + 32f + maxLogH) break;
                        GUI.Label(new Rect(chatX + 5f, logY, 330f, 18f), arr[i], _chatStyle);
                        logY += 18f;
                    }
                }

                _chatInput = GUI.TextField(
                    new Rect(chatX + 5f, chatY + 32f + maxLogH, 330f, 24f), _chatInput, _inputStyle);

                if (Event.current != null && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    if (!string.IsNullOrEmpty(_chatInput))
                    {
                        ParalivesMultiplayer.Networking.ChatManager.SendChat(_chatInput);
                        _chatInput = "";
                    }
                }
            }
        }
    }
}
