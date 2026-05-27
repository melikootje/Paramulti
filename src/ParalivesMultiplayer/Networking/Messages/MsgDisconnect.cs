using System;
using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgDisconnect : MessageBase
    {
        const string _code = "Disconnect";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public string Reason;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Reason);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgDisconnect();
            msg.Reason = input.ReadString();
            message = msg;
            return true;
        }
    }
}
