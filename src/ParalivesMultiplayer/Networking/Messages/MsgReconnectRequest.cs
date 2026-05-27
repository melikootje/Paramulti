using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgReconnectRequest : MessageBase
    {
        public override string MessageCode => "ReconnReq";
        public override byte[] MessageCodeBytes => Encoding.UTF8.GetBytes(MessageCode);

        public int PlayerId;
        public string ClientName;
        public uint LastKnownTick;
        public uint LastSequenceNumber;

        public override void Encode(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(ClientName);
            w.Write(LastKnownTick);
            w.Write(LastSequenceNumber);
        }

        public override bool TryDecode(BinaryReader r, out MessageBase message)
        {
            try
            {
                message = new MsgReconnectRequest
                {
                    PlayerId = r.ReadInt32(),
                    ClientName = r.ReadString(),
                    LastKnownTick = r.ReadUInt32(),
                    LastSequenceNumber = r.ReadUInt32()
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
