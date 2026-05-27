using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgConnect : MessageBase
    {
        const string _code = "Connect";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public string ClientName;

        public override void Encode(BinaryWriter output)
        {
            output.Write(ClientName);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgConnect();
            msg.ClientName = input.ReadString();
            message = msg;
            return true;
        }
    }
}
