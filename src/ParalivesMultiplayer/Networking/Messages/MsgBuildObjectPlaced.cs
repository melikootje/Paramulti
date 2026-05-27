using System.IO;
using System.Text;
using UnityEngine;

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
        public Vector3 Position;
        public Quaternion Rotation;
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
            msg.Position = input.ReadVector3();
            msg.Rotation = input.ReadQuaternion();
            msg.StyleName = input.ReadString();
            message = msg;
            return true;
        }
    }
}
