using System.Collections.Generic;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Networking.Messages;
using UnityEngine;

namespace ParalivesMultiplayer.Session
{
    public static class BuildCursorSyncManager
    {
        static readonly Dictionary<int, RemoteCursor> _remoteCursors = new Dictionary<int, RemoteCursor>();
        static readonly object _lock = new object();
        static float _lastSendTime;
        const float SendInterval = 0.1f; // 10Hz cursor updates

        public static bool Enabled { get; set; } = true;

        public static void Initialize()
        {
            Plugin.Log.LogInfo("[CursorSync] Initialized");
        }

        public static void Update()
        {
            if (!Enabled || !MultiplayerSession.IsActive) return;

            var now = Time.time;
            if (now - _lastSendTime < SendInterval) return;
            _lastSendTime = now;

            // Only send cursor position if we're in build mode
            // We detect build mode by checking if BuildSyncManager is enabled
            // or by looking for build mode UI/camera
            if (IsInBuildMode())
            {
                SendLocalCursorPosition();
            }
        }

        static bool IsInBuildMode()
        {
            // Heuristic: check if BuildSyncManager is active, or look for build camera
            if (BuildSyncManager.Enabled) return true;

            // Fallback: look for build mode camera or UI
            var buildCam = GameObject.Find("BuildModeCamera");
            if (buildCam != null) return true;

            // Another heuristic: check if main camera has a specific build-mode component
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                var camGO = mainCam.gameObject;
                if (camGO.name.Contains("Build") || camGO.name.Contains("Edit"))
                    return true;
            }

            return false;
        }

        static void SendLocalCursorPosition()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Raycast from mouse into world
            var ray = cam.ScreenPointToRay(UnityEngine.Input.mousePosition);
            if (UnityEngine.Physics.Raycast(ray, out var hit, 1000f))
            {
                var msg = new MsgCursorPing
                {
                    PlayerId = MultiplayerSession.LocalPlayerId,
                    Position = hit.point.FromUnity(),
                    Tick = MultiplayerSession.Tick
                };

                var net = UdpNetworkManager.Instance;
                if (net == null) return;

                if (MultiplayerSession.IsHost)
                    net.SendToAllClients(msg);
                else
                    net.SendToHost(msg);

                Plugin.Log.LogDebug($"[CursorSync] Sent cursor ping at {hit.point}");
            }
        }

        public static void ReceiveCursorPing(MsgCursorPing msg)
        {
            if (msg.PlayerId == MultiplayerSession.LocalPlayerId) return;

            var pos = msg.Position.ToUnity();

            lock (_lock)
            {
                if (!_remoteCursors.TryGetValue(msg.PlayerId, out var cursor))
                {
                    cursor = CreateRemoteCursor(msg.PlayerId);
                    _remoteCursors[msg.PlayerId] = cursor;
                }

                cursor.TargetPosition = pos;
                cursor.LastUpdateTime = Time.time;
            }

            Plugin.Log.LogDebug($"[CursorSync] Received cursor ping from player {msg.PlayerId} at {pos}");
        }

        static RemoteCursor CreateRemoteCursor(int playerId)
        {
            var go = new GameObject($"[RemoteCursor:{playerId}]");
            go.tag = "Untagged";

            var transform = go.transform;
            transform.position = Vector3.zero;
            transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

            // Small colored sphere
            var filter = go.AddComponent<MeshFilter>();
            filter.mesh = CreateSphereMesh();

            var renderer = go.AddComponent<MeshRenderer>();
            var color = GetPlayerColor(playerId);
            var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
            mat.color = color;
            renderer.material = mat;

            // Add nameplate
            var np = go.AddComponent<NameplateBillboard>();
            np.PlayerName = $"P{playerId}";
            np.Color = color;
            np.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            Plugin.Log.LogInfo($"[CursorSync] Created remote cursor for player {playerId}");

            return new RemoteCursor
            {
                GameObject = go,
                Transform = transform,
                TargetPosition = Vector3.zero,
                LastUpdateTime = Time.time
            };
        }

        static Mesh CreateSphereMesh()
        {
            var mesh = new Mesh();
            mesh.name = "CursorSphere";
            var verts = new Vector3[12];
            float r = 0.5f;
            float phi = (1f + Mathf.Sqrt(5f)) / 2f;
            float a = r / Mathf.Sqrt(1 + phi * phi);
            float b = a * phi;

            verts[0] = new Vector3(-a, b, 0);
            verts[1] = new Vector3(a, b, 0);
            verts[2] = new Vector3(-a, -b, 0);
            verts[3] = new Vector3(a, -b, 0);
            verts[4] = new Vector3(0, -a, b);
            verts[5] = new Vector3(0, a, b);
            verts[6] = new Vector3(0, -a, -b);
            verts[7] = new Vector3(0, a, -b);
            verts[8] = new Vector3(b, 0, -a);
            verts[9] = new Vector3(b, 0, a);
            verts[10] = new Vector3(-b, 0, -a);
            verts[11] = new Vector3(-b, 0, a);

            var tris = new int[]
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,11,2, 6,10,2, 8,7,6, 9,5,4, 9,8,1
            };

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        static Color GetPlayerColor(int playerId)
        {
            var colors = new[]
            {
                Color.red, Color.green, Color.blue, Color.yellow,
                Color.cyan, Color.magenta, Color.white, Color.black
            };
            return colors[playerId % colors.Length];
        }

        public static void UpdateRemoteCursors()
        {
            lock (_lock)
            {
                var toRemove = new List<int>();
                foreach (var kv in _remoteCursors)
                {
                    var cursor = kv.Value;
                    if (Time.time - cursor.LastUpdateTime > 5f)
                    {
                        // Timeout - remove cursor
                        toRemove.Add(kv.Key);
                        if (cursor.GameObject != null)
                            Object.Destroy(cursor.GameObject);
                        continue;
                    }

                    // Smooth lerp to target
                    cursor.Transform.position = Vector3.Lerp(cursor.Transform.position, cursor.TargetPosition, Time.deltaTime * 10f);
                }

                foreach (var id in toRemove)
                    _remoteCursors.Remove(id);
            }
        }

        public static void ClearAll()
        {
            lock (_lock)
            {
                foreach (var kv in _remoteCursors)
                {
                    if (kv.Value.GameObject != null)
                        Object.Destroy(kv.Value.GameObject);
                }
                _remoteCursors.Clear();
            }
            Plugin.Log.LogInfo("[CursorSync] Cleared all remote cursors");
        }
    }

    class RemoteCursor
    {
        public GameObject GameObject;
        public Transform Transform;
        public Vector3 TargetPosition;
        public float LastUpdateTime;
    }
}
