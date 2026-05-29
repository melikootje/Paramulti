using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgSelectCharacter : MessageBase
    {
        const string _code = "SelectChar";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public ulong CharacterGuid;
        public bool Selected;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write((ulong)CharacterGuid);
            output.Write(Selected);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgSelectCharacter();
            msg.PlayerId = input.ReadInt32();
            msg.CharacterGuid = input.ReadUInt64();
            msg.Selected = input.ReadBoolean();
            message = msg;
            return true;
        }
    }
}
