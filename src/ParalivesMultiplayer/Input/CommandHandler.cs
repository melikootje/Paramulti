using System;
using System.Collections.Generic;
using System.Reflection;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;
using UnityEngine;

namespace ParalivesMultiplayer.Input
{
    public static class CommandHandler
    {
        static bool _initialized;
        static Type _keyboardType;
        static PropertyInfo _keyboardCurrentProp;
        static readonly Dictionary<string, PropertyInfo> _keyPropCache = new Dictionary<string, PropertyInfo>();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        var t = asm.GetType("UnityEngine.InputSystem.Keyboard");
                        if (t != null)
                        {
                            _keyboardType = t;
                            _keyboardCurrentProp = t.GetProperty("current",
                                BindingFlags.Public | BindingFlags.Static);
                            PreloadKeyProperty("f5Key");
                            PreloadKeyProperty("f6Key");
                            PreloadKeyProperty("f7Key");
                            PreloadKeyProperty("f8Key");
                            PreloadKeyProperty("f9Key");
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                try { Plugin.Log?.LogWarning($"[Cmd] InputSystem init: {ex.Message}"); } catch { }
            }

            bool ready = _keyboardType != null && _keyboardCurrentProp != null;
            try { Plugin.Log?.LogInfo($"[Cmd] Initialized. InputSystem={ready}. F5=Host, F6=Client, F7=Disconnect, F8=ForceSendCharData, F9=SpawnTestProxy."); } catch { }
        }

        static void PreloadKeyProperty(string propName)
        {
            if (_keyboardType == null) return;
            try
            {
                var prop = _keyboardType.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                    _keyPropCache[propName] = prop;
            }
            catch { }
        }

        public static void ProcessInput()
        {
            if (!_initialized) return;

            if (_keyboardType != null && _keyboardCurrentProp != null)
            {
                try
                {
                    var keyboard = _keyboardCurrentProp.GetValue(null);
                    if (keyboard != null)
                    {
                        if (IsKeyPressedThisFrame(keyboard, "f5Key"))
                        {
                            try { Plugin.Log?.LogInfo("[Cmd] F5 pressed (InputSystem)"); } catch { }
                            StartHost();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f6Key"))
                        {
                            try { Plugin.Log?.LogInfo("[Cmd] F6 pressed (InputSystem)"); } catch { }
                            StartClient();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f7Key"))
                        {
                            try { Plugin.Log?.LogInfo("[Cmd] F7 pressed (InputSystem)"); } catch { }
                            StopHost();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f8Key"))
                        {
                            try { Plugin.Log?.LogInfo("[Cmd] F8 pressed (InputSystem) — forcing character data sync"); } catch { }
                            ForceSendCharacterData();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f9Key"))
                        {
                            try { Plugin.Log?.LogInfo("[Cmd] F9 pressed (InputSystem) — spawning test proxy"); } catch { }
                            SpawnTestProxy();
                            return;
                        }
                    }
                }
                catch { }
            }

            // Legacy input fallback
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
            {
                try { Plugin.Log?.LogInfo("[Cmd] F5 pressed (Legacy)"); } catch { }
                StartHost();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
            {
                try { Plugin.Log?.LogInfo("[Cmd] F6 pressed (Legacy)"); } catch { }
                StartClient();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                try { Plugin.Log?.LogInfo("[Cmd] F7 pressed (Legacy)"); } catch { }
                StopHost();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                try { Plugin.Log?.LogInfo("[Cmd] F8 pressed (Legacy) — forcing character data sync"); } catch { }
                ForceSendCharacterData();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                try { Plugin.Log?.LogInfo("[Cmd] F9 pressed (Legacy) — spawning test proxy"); } catch { }
                SpawnTestProxy();
            }
        }

        static bool IsKeyPressedThisFrame(object keyboard, string propName)
        {
            try
            {
                PropertyInfo prop;
                if (!_keyPropCache.TryGetValue(propName, out prop))
                {
                    prop = _keyboardType?.GetProperty(propName,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null) return false;
                    _keyPropCache[propName] = prop;
                }

                var keyControl = prop.GetValue(keyboard);
                if (keyControl == null) return false;

                var wasPressedProp = keyControl.GetType().GetProperty("wasPressedThisFrame",
                    BindingFlags.Public | BindingFlags.Instance);
                if (wasPressedProp == null) return false;

                return (bool)wasPressedProp.GetValue(keyControl);
            }
            catch { return false; }
        }

        static void StartHost()
        {
            if (MultiplayerSession.IsActive) return;

            try
            {
                UdpNetworkManager.Instance?.Dispose();
                var mgr = new TcpNetworkManager();
                Session.RemoteCharacterManager.Initialize();
                Session.PlayerSyncManager.Initialize();
                MultiplayerSession.StartAsHost();
                mgr.StartHost(Plugin.ListenPort);
                Plugin.Log?.LogInfo($"[Cmd] Started HOST on port {Plugin.ListenPort}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Cmd] Host failed: {ex.Message}");
            }
        }

        static void StartClient()
        {
            if (MultiplayerSession.IsActive) return;

            try
            {
                UdpNetworkManager.Instance?.Dispose();
                var mgr = new TcpNetworkManager();
                MultiplayerSession.StartAsClient();
                mgr.StartClient(Plugin.ConnectAddress, Plugin.ListenPort);
                Plugin.Log?.LogInfo($"[Cmd] Connecting CLIENT to {Plugin.ConnectAddress}:{Plugin.ListenPort}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Cmd] Client failed: {ex.Message}");
            }
        }

        static void StopHost()
        {
            if (!MultiplayerSession.IsActive) return;

            try
            {
                UdpNetworkManager.Instance?.Dispose();
                Session.RemoteCharacterManager.OnSessionEnd();
                Session.PlayerSyncManager.ClearAll();
                MultiplayerSession.End();
                Plugin.Log?.LogInfo("[Cmd] Host stopped.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Cmd] Stop error: {ex.Message}");
            }
        }

        static void ForceSendCharacterData()
        {
            try
            {
                var net = UdpNetworkManager.Instance;
                if (net == null || !MultiplayerSession.IsActive)
                {
                    Plugin.Log?.LogWarning("[Cmd] Cannot force send: network or session not active");
                    return;
                }

                // Force FindLocalCharacter first
                Session.RemoteCharacterManager.FindLocalCharacter();

                var charData = Session.RemoteCharacterManager.BuildLocalCharacterDataSync();
                if (charData == null)
                {
                    Plugin.Log?.LogWarning("[Cmd] Force send: BuildLocalCharacterDataSync returned null");
                    return;
                }

                if (MultiplayerSession.IsHost)
                {
                    net.SendToAllClients(charData);
                    Plugin.Log?.LogInfo($"[Cmd] Force-sent character data to all clients");
                }
                else
                {
                    net.SendToHost(charData);
                    Plugin.Log?.LogInfo("[Cmd] Force-sent character data to host");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Cmd] ForceSendCharacterData error: {ex.Message}");
            }
        }

        static void SpawnTestProxy()
        {
            try
            {
                var localTransform = Session.RemoteCharacterManager.LocalCharacterTransform;
                Vector3 spawnPos;
                if (localTransform != null)
                    spawnPos = localTransform.position + new UnityEngine.Vector3(2f, 0f, 2f);
                else
                    spawnPos = new UnityEngine.Vector3(0f, 1f, 0f);

                var entry = Session.RemoteCharacterManager.CreateTestProxy(999, "TestProxy", spawnPos);
                if (entry != null)
                {
                    Plugin.Log?.LogInfo($"[Cmd] Spawned test proxy at {spawnPos}. Look around — it should be a brightly colored capsule with a nameplate.");
                }
                else
                {
                    Plugin.Log?.LogWarning("[Cmd] Test proxy creation returned null");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Cmd] SpawnTestProxy error: {ex.Message}");
            }
        }
    }
}
