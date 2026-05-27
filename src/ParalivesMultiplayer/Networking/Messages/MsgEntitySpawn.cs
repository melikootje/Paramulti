using System.IO;
using System.Text;
using UnityEngine;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgEntitySpawn : MessageBase
    {
        const string _code = "EntitySpawn";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public uint Tick;
        public uint EntityId;
        public string EntityType;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Tick);
            output.Write(EntityId);
            output.Write(EntityType);
            output.Write(Position);
            output.Write(Rotation);
            output.Write(Scale);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgEntitySpawn();
            msg.PlayerId = input.ReadInt32();
            msg.Tick = input.ReadUInt32();
            msg.EntityId = input.ReadUInt32();
            msg.EntityType = input.ReadString();
            msg.Position = input.ReadVector3();
            msg.Rotation = input.ReadQuaternion();
            msg.Scale = input.ReadVector3();
            message = msg;
            return true;
        }
    }
}
