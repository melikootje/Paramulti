using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgPlayerJoin : MessageBase
    {
        const string _code = "PlayerJoin";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public string PlayerName;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(PlayerName);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgPlayerJoin();
            msg.PlayerId = input.ReadInt32();
            msg.PlayerName = input.ReadString();
            message = msg;
            return true;
        }
    }
}
