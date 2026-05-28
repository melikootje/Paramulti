using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgBuildObjectPlaced : MessageBase
    {
        const string _code = "BuildObjPlace";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public uint Tick;
        public uint SequenceNumber;
        public string ObjectTypeId;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public string StyleName;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Tick);
            output.Write(SequenceNumber);
            output.Write(ObjectTypeId);
            output.Write(Position);
            output.Write(Rotation);
            output.Write(StyleName);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgBuildObjectPlaced();
            msg.PlayerId = input.ReadInt32();
            msg.Tick = input.ReadUInt32();
            msg.SequenceNumber = input.ReadUInt32();
            msg.ObjectTypeId = input.ReadString();
            msg.Position = input.ReadNetVector3();
            msg.Rotation = input.ReadNetQuaternion();
            msg.StyleName = input.ReadString();
            message = msg;
            return true;
        }
    }
}
