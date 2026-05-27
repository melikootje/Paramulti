using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgSyncState : MessageBase
    {
        const string _code = "SyncState";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public int PlayerCount;
        public float[] PlayerPositionsX;
        public float[] PlayerPositionsY;
        public float[] PlayerPositionsZ;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(PlayerCount);
            if (PlayerPositionsX != null)
            {
                foreach (var v in PlayerPositionsX) output.Write(v);
            }
            if (PlayerPositionsY != null)
            {
                foreach (var v in PlayerPositionsY) output.Write(v);
            }
            if (PlayerPositionsZ != null)
            {
                foreach (var v in PlayerPositionsZ) output.Write(v);
            }
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgSyncState();
            msg.Tick = input.ReadUInt32();
            msg.PlayerCount = input.ReadInt32();
            msg.PlayerPositionsX = new float[msg.PlayerCount];
            msg.PlayerPositionsY = new float[msg.PlayerCount];
            msg.PlayerPositionsZ = new float[msg.PlayerCount];
            for (int i = 0; i < msg.PlayerCount; i++)
                msg.PlayerPositionsX[i] = input.ReadSingle();
            for (int i = 0; i < msg.PlayerCount; i++)
                msg.PlayerPositionsY[i] = input.ReadSingle();
            for (int i = 0; i < msg.PlayerCount; i++)
                msg.PlayerPositionsZ[i] = input.ReadSingle();
            message = msg;
            return true;
        }
    }
}
