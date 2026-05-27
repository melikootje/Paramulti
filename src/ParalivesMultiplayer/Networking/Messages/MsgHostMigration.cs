using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgHostMigration : MessageBase
    {
        public override string MessageCode => "HostMigr";
        public override byte[] MessageCodeBytes => Encoding.UTF8.GetBytes(MessageCode);

        public int NewHostPlayerId;
        public string NewHostAddress;
        public int NewHostPort;
        public uint SessionTick;

        public override void Encode(BinaryWriter w)
        {
            w.Write(NewHostPlayerId);
            w.Write(NewHostAddress);
            w.Write(NewHostPort);
            w.Write(SessionTick);
        }

        public override bool TryDecode(BinaryReader r, out MessageBase message)
        {
            try
            {
                message = new MsgHostMigration
                {
                    NewHostPlayerId = r.ReadInt32(),
                    NewHostAddress = r.ReadString(),
                    NewHostPort = r.ReadInt32(),
                    SessionTick = r.ReadUInt32()
                };
                return true;
            }
            catch
            {
                message = null;
                return false;
            }
        }
    }
}
