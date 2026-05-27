using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgLoadInitiate : MessageBase
    {
        const string _code = "LoadInit";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public string SceneName;
        public int TotalChunks;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(SceneName);
            output.Write(TotalChunks);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgLoadInitiate();
            msg.Tick = input.ReadUInt32();
            msg.SceneName = input.ReadString();
            msg.TotalChunks = input.ReadInt32();
            message = msg;
            return true;
        }
    }

    public class MsgLoadStateChunk : MessageBase
    {
        const string _code = "LoadChunk";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int ChunkIndex;
        public int TotalChunks;
        public System.Collections.Generic.List<EntitySnapshotEntry> Entities;

        public MsgLoadStateChunk()
        {
            Entities = new System.Collections.Generic.List<EntitySnapshotEntry>();
        }

        public override void Encode(BinaryWriter output)
        {
            output.Write(ChunkIndex);
            output.Write(TotalChunks);
            output.Write(Entities.Count);
            foreach (var e in Entities)
                e.Encode(output);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgLoadStateChunk();
            msg.ChunkIndex = input.ReadInt32();
            msg.TotalChunks = input.ReadInt32();
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

    public class MsgLoadComplete : MessageBase
    {
        const string _code = "LoadComp";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public uint Tick;
        public bool Success;
        public int ChunksReceived;
        public int TotalChunks;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Tick);
            output.Write(Success);
            output.Write(ChunksReceived);
            output.Write(TotalChunks);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgLoadComplete();
            msg.PlayerId = input.ReadInt32();
            msg.Tick = input.ReadUInt32();
            msg.Success = input.ReadBoolean();
            msg.ChunksReceived = input.ReadInt32();
            msg.TotalChunks = input.ReadInt32();
            message = msg;
            return true;
        }
    }
}
