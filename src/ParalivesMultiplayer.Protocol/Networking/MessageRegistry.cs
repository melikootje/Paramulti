using System;
using System.Collections.Generic;

namespace ParalivesMultiplayer.Networking
{
    public static class MessageRegistry
    {
        static readonly Dictionary<string, MessageBase> _registry = new Dictionary<string, MessageBase>();
        static readonly object _lock = new object();

        public static Action<string> LogAction { get; set; } = _ => { };

        public static void Register(MessageBase prototype)
        {
            if (prototype == null) return;
            string code = prototype.MessageCode;
            lock (_lock)
            {
                if (_registry.ContainsKey(code))
                    LogAction($"[MessageRegistry] Overriding handler for \"{code}\"");
                _registry[code] = prototype;
                LogAction($"[MessageRegistry] Registered: {code} -> {prototype.GetType().Name}");
            }
        }

        public static bool TryGetHandler(string code, out MessageBase prototype)
        {
            lock (_lock)
                return _registry.TryGetValue(code, out prototype);
        }

        public static void RegisterAll()
        {
            Register(new Messages.MsgConnect());
            Register(new Messages.MsgDisconnect());
            Register(new Messages.MsgPlayerJoin());
            Register(new Messages.MsgPlayerLeave());
            Register(new Messages.MsgSyncState());
            Register(new Messages.MsgUpdateState());
            Register(new Messages.MsgChat());
            Register(new Messages.MsgCursorPing());
            Register(new Messages.MsgBuildObjectPlaced());
            Register(new Messages.MsgEntitySpawn());
            Register(new Messages.MsgEntityDespawn());
            Register(new Messages.MsgRequestFullState());
            Register(new Messages.MsgFullStateSnapshot());
            Register(new Messages.MsgReadyCheck());
            Register(new Messages.MsgInputCommand());
            Register(new Messages.MsgBuildModeEvent());
            Register(new Messages.MsgHeartbeat());
            Register(new Messages.MsgSaveInitiate());
            Register(new Messages.MsgSaveAck());
            Register(new Messages.MsgSaveComplete());
            Register(new Messages.MsgLoadInitiate());
            Register(new Messages.MsgLoadStateChunk());
            Register(new Messages.MsgLoadComplete());
            Register(new Messages.MsgReconnectRequest());
            Register(new Messages.MsgReconnectAck());
            Register(new Messages.MsgHostMigration());
            Register(new Messages.MsgPing());
            Register(new Messages.MsgPong());
            LogAction($"[MessageRegistry] Registered {_registry.Count} message types.");
        }
    }
}
