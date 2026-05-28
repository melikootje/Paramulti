using System;
using System.Linq;
using ParalivesMultiplayer.Networking;
using ParalivesMultiplayer.Session;

namespace ParalivesMultiplayer.Input
{
    public static class CommandHandler
    {
        static bool _initialized;
        static object _keyboard;
        static bool _inputSystemReady;
        static Func<object, string, bool> _wasPressedThisFrame;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                var inputSystemAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Unity.InputSystem");

                if (inputSystemAssembly != null)
                {
                    var keyboardType = inputSystemAssembly.GetType("UnityEngine.InputSystem.Keyboard");
                    if (keyboardType != null)
                    {
                        var currentProp = keyboardType.GetProperty("current");
                        _keyboard = currentProp?.GetValue(null);

                        if (_keyboard != null)
                        {
                            var keyBindingType = keyboardType.GetProperties()
                                .FirstOrDefault(p => p.Name == "f5Key" && p.PropertyType.Name.Contains("Key"));

                            if (keyBindingType != null)
                            {
                                var keyType = keyBindingType.PropertyType;
                                var wasPressedProp = keyType.GetProperty("wasPressedThisFrame");
                                if (wasPressedProp != null)
                                {
                                    _wasPressedThisFrame = (kb, keyName) =>
                                    {
                                        try
                                        {
                                            var keyProp = kb.GetType().GetProperty(keyName);
                                            var keyObj = keyProp?.GetValue(kb);
                                            return (bool)keyObj.GetType().GetProperty("wasPressedThisFrame")?.GetValue(keyObj);
                                        }
                                        catch { return false; }
                                    };
                                }
                            }

                            _inputSystemReady = _wasPressedThisFrame != null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { Plugin.Log?.LogWarning($"[Cmd] InputSystem init failed: {ex.Message}"); } catch {}
            }

            try { Plugin.Log?.LogInfo($"[Cmd] Initialized. InputSystem={_inputSystemReady}. F5=Host, F6=Client, F7=Disconnect."); } catch {}
        }

        public static void ProcessInput()
        {
            if (!_initialized) return;

            bool keyHandled = false;

            if (_inputSystemReady && _keyboard != null && _wasPressedThisFrame != null)
            {
                if (_wasPressedThisFrame(_keyboard, "f5Key"))
                {
                    try { Plugin.Log?.LogInfo("[Cmd] F5 pressed (InputSystem)"); } catch {}
                    StartHost();
                    keyHandled = true;
                }
                else if (_wasPressedThisFrame(_keyboard, "f6Key"))
                {
                    try { Plugin.Log?.LogInfo("[Cmd] F6 pressed (InputSystem)"); } catch {}
                    StartClient();
                    keyHandled = true;
                }
                else if (_wasPressedThisFrame(_keyboard, "f7Key"))
                {
                    try { Plugin.Log?.LogInfo("[Cmd] F7 pressed (InputSystem)"); } catch {}
                    Disconnect();
                    keyHandled = true;
                }
            }

            if (!keyHandled)
            {
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
                {
                    try { Plugin.Log?.LogInfo("[Cmd] F5 pressed (Legacy)"); } catch {}
                    StartHost();
                    keyHandled = true;
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
                {
                    try { Plugin.Log?.LogInfo("[Cmd] F6 pressed (Legacy)"); } catch {}
                    StartClient();
                    keyHandled = true;
                }
                else if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
                {
                    try { Plugin.Log?.LogInfo("[Cmd] F7 pressed (Legacy)"); } catch {}
                    Disconnect();
                    keyHandled = true;
                }
            }
        }

        static void StartHost()
        {
            if (MultiplayerSession.IsActive) return;

            try
            {
                TcpNetworkManager.Instance?.Dispose();
                var mgr = new TcpNetworkManager();
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
                TcpNetworkManager.Instance?.Dispose();
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

        static void Disconnect()
        {
            if (!MultiplayerSession.IsActive) return;

            try
            {
                TcpNetworkManager.Instance?.Dispose();
                MultiplayerSession.End();
                Plugin.Log?.LogInfo("[Cmd] Disconnected.");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[Cmd] Disconnect error: {ex.Message}");
            }
        }
    }
}
