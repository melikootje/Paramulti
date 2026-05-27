using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgReadyCheck : MessageBase
    {
        const string _code = "ReadyCheck";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public bool IsReady;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(IsReady);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgReadyCheck();
            msg.PlayerId = input.ReadInt32();
            msg.IsReady = input.ReadBoolean();
            message = msg;
            return true;
        }
    }
}
