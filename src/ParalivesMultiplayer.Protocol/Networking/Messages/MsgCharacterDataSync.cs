using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgCharacterDataSync : MessageBase
    {
        const string _code = "CharData";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public ulong CharacterGuid;
        public ulong CharacterModelGuid;
        public string FirstName;
        public string FullName;
        public float Age;
        public ulong SpeciesGuid;
        public ulong GenderGuid;
        public ulong CurrentPostureGuid;
        public bool IsDeadOrTakenAway;
        public NetVector3 LastKnownPosition;
        public NetQuaternion LastKnownRotation;
        public string CharacterVisualJson;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(CharacterGuid);
            output.Write(CharacterModelGuid);
            output.Write(FirstName ?? "");
            output.Write(FullName ?? "");
            output.Write(Age);
            output.Write(SpeciesGuid);
            output.Write(GenderGuid);
            output.Write(CurrentPostureGuid);
            output.Write(IsDeadOrTakenAway);
            output.Write(LastKnownPosition);
            output.Write(LastKnownRotation);
            output.Write(CharacterVisualJson ?? "");
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgCharacterDataSync();
            msg.PlayerId = input.ReadInt32();
            msg.CharacterGuid = input.ReadUInt64();
            msg.CharacterModelGuid = input.ReadUInt64();
            msg.FirstName = input.ReadString();
            msg.FullName = input.ReadString();
            msg.Age = input.ReadSingle();
            msg.SpeciesGuid = input.ReadUInt64();
            msg.GenderGuid = input.ReadUInt64();
            msg.CurrentPostureGuid = input.ReadUInt64();
            msg.IsDeadOrTakenAway = input.ReadBoolean();
            msg.LastKnownPosition = input.ReadNetVector3();
            msg.LastKnownRotation = input.ReadNetQuaternion();
            msg.CharacterVisualJson = input.BaseStream.Position < input.BaseStream.Length
                ? input.ReadString()
                : "";
            message = msg;
            return true;
        }
    }
}
