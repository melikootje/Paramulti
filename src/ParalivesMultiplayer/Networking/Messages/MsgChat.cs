using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgChat : MessageBase
    {
        const string _code = "Chat";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public string PlayerName;
        public string Message;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerName);
            output.Write(Message);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgChat();
            msg.PlayerName = input.ReadString();
            msg.Message = input.ReadString();
            message = msg;
            return true;
        }
    }
}
