using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgPing : MessageBase
    {
        public override string MessageCode => "Ping";
        public override byte[] MessageCodeBytes => Encoding.UTF8.GetBytes(MessageCode);

        public int PlayerId;
        public long TimestampMs;

        public override void Encode(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(TimestampMs);
        }

        public override bool TryDecode(BinaryReader r, out MessageBase message)
        {
            try
            {
                message = new MsgPing
                {
                    PlayerId = r.ReadInt32(),
                    TimestampMs = r.ReadInt64()
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
