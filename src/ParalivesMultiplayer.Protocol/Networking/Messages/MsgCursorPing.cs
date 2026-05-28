using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgCursorPing : MessageBase
    {
        const string _code = "CursorPing";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public NetVector3 Position;
        public uint Tick;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Position);
            output.Write(Tick);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgCursorPing();
            msg.PlayerId = input.ReadInt32();
            msg.Position = input.ReadNetVector3();
            msg.Tick = input.ReadUInt32();
            message = msg;
            return true;
        }
    }
}
