using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;
using UnityEngine;

namespace ParalivesMultiplayer.Input
{
    public static class CommandHandler
    {
        const string INPUTLOG = "BepInEx/Paramulti_Input.log";
        static bool _initialized;
        static Type _keyboardType;
        static PropertyInfo _keyboardCurrentProp;
        static readonly Dictionary<string, PropertyInfo> _keyPropCache = new Dictionary<string, PropertyInfo>();

        static void LogInput(string msg)
        {
            try { File.AppendAllText(INPUTLOG, $"[{DateTime.Now:O}] {msg}\n"); } catch { }
            try { Plugin.Log?.LogInfo(msg); } catch { }
        }

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
                            // Enumerate ALL public instance properties of Keyboard for diagnostics
                            try
                            {
                                File.WriteAllText(INPUTLOG, $"[{DateTime.Now:O}] Keyboard type found: {t.AssemblyQualifiedName}\n");
                                File.AppendAllText(INPUTLOG, $"[{DateTime.Now:O}] Properties on Keyboard:\n");
                                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                                    File.AppendAllText(INPUTLOG, $"  - {p.PropertyType.Name} {p.Name}\n");
                                File.AppendAllText(INPUTLOG, $"[{DateTime.Now:O}] current prop = {_keyboardCurrentProp?.Name ?? "null"}\n");
                                // Try to get current
                                try
                                {
                                    var kb = _keyboardCurrentProp?.GetValue(null);
                                    File.AppendAllText(INPUTLOG, $"[{DateTime.Now:O}] current keyboard = {kb}\n");
                                }
                                catch (Exception ex) { File.AppendAllText(INPUTLOG, $"[{DateTime.Now:O}] current get failed: {ex.Message}\n"); }
                            }
                            catch (Exception ex) { File.AppendAllText(INPUTLOG, $"[{DateTime.Now:O}] Keyboard enum failed: {ex.Message}\n"); }

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
            LogInput($"Initialize done. InputSystem ready={ready}, keyboardType={_keyboardType?.FullName ?? "null"}, cached keys: {string.Join(",", _keyPropCache.Keys)}");
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

            // Log that we're being called (throttled)
            DateTime now = DateTime.Now;
            if ((now - _lastProcessLog).TotalSeconds >= 3.0)
            {
                _lastProcessLog = now;
                LogInput($"ProcessInput called; session active={MultiplayerSession.IsActive}; keyboardType={_keyboardType?.Name ?? "null"}; currentProp={_keyboardCurrentProp?.Name ?? "null"}");
            }

            if (_keyboardType != null && _keyboardCurrentProp != null)
            {
                try
                {
                    var keyboard = _keyboardCurrentProp.GetValue(null);
                    if (keyboard != null)
                    {
                        if (IsKeyPressedThisFrame(keyboard, "f5Key"))
                        {
                            LogInput("[Cmd] F5 pressed (InputSystem) — calling StartHost()");
                            StartHost();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f6Key"))
                        {
                            LogInput("[Cmd] F6 pressed (InputSystem) — calling StartClient()");
                            StartClient();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f7Key"))
                        {
                            LogInput("[Cmd] F7 pressed (InputSystem) — calling StopHost()");
                            StopHost();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f8Key"))
                        {
                            LogInput("[Cmd] F8 pressed (InputSystem) — calling ForceSendCharacterData()");
                            ForceSendCharacterData();
                            return;
                        }
                        if (IsKeyPressedThisFrame(keyboard, "f9Key"))
                        {
                            LogInput("[Cmd] F9 pressed (InputSystem) — calling SpawnTestProxy()");
                            SpawnTestProxy();
                            return;
                        }
                    }
                    else
                    {
                        if ((now - _lastNullKbLog).TotalSeconds >= 5.0)
                        {
                            _lastNullKbLog = now;
                            LogInput("Keyboard.current is null (no keyboard detected)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogInput($"InputSystem check exception: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Legacy input fallback
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
            {
                LogInput("[Cmd] F5 pressed (Legacy) — calling StartHost()");
                StartHost();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
            {
                LogInput("[Cmd] F6 pressed (Legacy) — calling StartClient()");
                StartClient();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                LogInput("[Cmd] F7 pressed (Legacy) — calling StopHost()");
                StopHost();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                LogInput("[Cmd] F8 pressed (Legacy) — calling ForceSendCharacterData()");
                ForceSendCharacterData();
            }
            else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                LogInput("[Cmd] F9 pressed (Legacy) — calling SpawnTestProxy()");
                SpawnTestProxy();
            }
        }

        static DateTime _lastProcessLog = DateTime.MinValue;
        static DateTime _lastNullKbLog = DateTime.MinValue;

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
                var mgr = new UdpNetworkManager();
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
                var mgr = new UdpNetworkManager();
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
