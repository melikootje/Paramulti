using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgRosterSync : MessageBase
    {
        const string _code = "RosterSync";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int[] PlayerIds;
        public string[] PlayerNames;

        public override void Encode(BinaryWriter output)
        {
            var count = PlayerIds != null ? PlayerIds.Length : 0;
            output.Write(count);
            for (int i = 0; i < count; i++)
            {
                output.Write(PlayerIds[i]);
                output.Write(PlayerNames != null && i < PlayerNames.Length ? PlayerNames[i] : "");
            }
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgRosterSync();
            var count = input.ReadInt32();
            msg.PlayerIds = new int[count];
            msg.PlayerNames = new string[count];
            for (int i = 0; i < count; i++)
            {
                msg.PlayerIds[i] = input.ReadInt32();
                msg.PlayerNames[i] = input.ReadString();
            }
            message = msg;
            return true;
        }
    }
}
