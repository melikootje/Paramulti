using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgFullStateSnapshot : MessageBase
    {
        const string _code = "FullStateSnap";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public int PlayerCount;
        public List<EntitySnapshotEntry> Entities;

        public MsgFullStateSnapshot()
        {
            Entities = new List<EntitySnapshotEntry>();
        }

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(PlayerCount);
            output.Write(Entities.Count);
            foreach (var e in Entities)
                e.Encode(output);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgFullStateSnapshot();
            msg.Tick = input.ReadUInt32();
            msg.PlayerCount = input.ReadInt32();
            int count = input.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var entry = new EntitySnapshotEntry();
                if (!entry.TryDecode(input))
                {
                    message = null;
                    return false;
                }
                msg.Entities.Add(entry);
            }
            message = msg;
            return true;
        }
    }

    public class EntitySnapshotEntry
    {
        public uint EntityId;
        public string EntityType;
        public UnityEngine.Vector3 Position;
        public UnityEngine.Quaternion Rotation;
        public UnityEngine.Vector3 Scale;
        public int OwnerPlayerId;

        public void Encode(BinaryWriter output)
        {
            output.Write(EntityId);
            output.Write(EntityType);
            output.Write(Position);
            output.Write(Rotation);
            output.Write(Scale);
            output.Write(OwnerPlayerId);
        }

        public bool TryDecode(BinaryReader input)
        {
            try
            {
                EntityId = input.ReadUInt32();
                EntityType = input.ReadString();
                Position = input.ReadVector3();
                Rotation = input.ReadQuaternion();
                Scale = input.ReadVector3();
                OwnerPlayerId = input.ReadInt32();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
