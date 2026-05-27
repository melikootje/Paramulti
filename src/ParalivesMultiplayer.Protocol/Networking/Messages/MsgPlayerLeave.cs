using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgPlayerLeave : MessageBase
    {
        const string _code = "PlayerLeave";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgPlayerLeave();
            msg.PlayerId = input.ReadInt32();
            message = msg;
            return true;
        }
    }
}
