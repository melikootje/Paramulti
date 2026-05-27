using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking.Messages;

namespace ParalivesMultiplayer.Networking
{
    public abstract class MessageBase
    {
        public int SenderClientId { get; set; }
        public Action<MessageBase> OnReceive { get; set; }

        public abstract string MessageCode { get; }
        public abstract byte[] MessageCodeBytes { get; }
        public abstract void Encode(BinaryWriter output);
        public abstract bool TryDecode(BinaryReader input, out MessageBase message);
    }

    public static class MessageRegistry
    {
        static readonly Dictionary<string, MessageBase> _registry = new Dictionary<string, MessageBase>();

        public static void Register(MessageBase prototype)
        {
            if (prototype == null) return;
            string code = prototype.MessageCode;
            if (_registry.ContainsKey(code))
            {
                Plugin.Log.LogWarning($"[MessageRegistry] Overriding handler for \"{code}\"");
            }
            _registry[code] = prototype;
            Plugin.Log.LogInfo($"[MessageRegistry] Registered: {code} -> {prototype.GetType().Name}");
        }

        public static bool TryGetHandler(string code, out MessageBase prototype)
        {
            return _registry.TryGetValue(code, out prototype);
        }

        public static void RegisterAll()
        {
            Register(new MsgConnect());
            Register(new MsgDisconnect());
            Register(new MsgPlayerJoin());
            Register(new MsgPlayerLeave());
            Register(new MsgSyncState());
            Register(new MsgUpdateState());
            Register(new MsgChat());
            Register(new MsgCursorPing());
            Register(new MsgBuildObjectPlaced());
            Register(new MsgEntitySpawn());
            Register(new MsgEntityDespawn());
            Register(new MsgRequestFullState());
            Register(new MsgFullStateSnapshot());
            Register(new MsgReadyCheck());
            Register(new MsgInputCommand());
            Register(new MsgBuildModeEvent());
            Register(new MsgHeartbeat());
            Plugin.Log.LogInfo($"[MessageRegistry] Registered {_registry.Count} message types.");
        }
    }
}
