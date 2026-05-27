using System;
using System.IO;

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
}
