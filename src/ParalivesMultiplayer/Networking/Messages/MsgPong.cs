using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgPong : MessageBase
    {
        public override string MessageCode => "Pong";
        public override byte[] MessageCodeBytes => Encoding.UTF8.GetBytes(MessageCode);

        public int PlayerId;
        public long OriginalTimestampMs;
        public long ReplyTimestampMs;

        public override void Encode(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(OriginalTimestampMs);
            w.Write(ReplyTimestampMs);
        }

        public override bool TryDecode(BinaryReader r, out MessageBase message)
        {
            try
            {
                message = new MsgPong
                {
                    PlayerId = r.ReadInt32(),
                    OriginalTimestampMs = r.ReadInt64(),
                    ReplyTimestampMs = r.ReadInt64()
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
