using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgRequestFullState : MessageBase
    {
        const string _code = "ReqFullState";
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
            var msg = new MsgRequestFullState();
            msg.PlayerId = input.ReadInt32();
            message = msg;
            return true;
        }
    }
}
