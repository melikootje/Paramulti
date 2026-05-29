using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgInteractionRequest : MessageBase
    {
        const string _code = "InteractReq";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int RequesterPlayerId;
        public ulong RequesterCharacterGuid;
        public ulong TargetCharacterGuid;
        public ulong InteractionGuid;
        public bool IsSocial;
        public bool IsAutonomous;

        public override void Encode(BinaryWriter output)
        {
            output.Write(RequesterPlayerId);
            output.Write((ulong)RequesterCharacterGuid);
            output.Write((ulong)TargetCharacterGuid);
            output.Write((ulong)InteractionGuid);
            output.Write(IsSocial);
            output.Write(IsAutonomous);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgInteractionRequest();
            msg.RequesterPlayerId = input.ReadInt32();
            msg.RequesterCharacterGuid = input.ReadUInt64();
            msg.TargetCharacterGuid = input.ReadUInt64();
            msg.InteractionGuid = input.ReadUInt64();
            msg.IsSocial = input.ReadBoolean();
            msg.IsAutonomous = input.ReadBoolean();
            message = msg;
            return true;
        }
    }
}
