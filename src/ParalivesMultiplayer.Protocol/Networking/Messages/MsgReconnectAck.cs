using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgReconnectAck : MessageBase
    {
        public override string MessageCode => "ReconnAck";
        public override byte[] MessageCodeBytes => Encoding.UTF8.GetBytes(MessageCode);

        public int PlayerId;
        public bool Allowed;
        public uint SessionTick;
        public string ErrorMessage;

        public override void Encode(BinaryWriter w)
        {
            w.Write(PlayerId);
            w.Write(Allowed);
            w.Write(SessionTick);
            w.Write(ErrorMessage);
        }

        public override bool TryDecode(BinaryReader r, out MessageBase message)
        {
            try
            {
                message = new MsgReconnectAck
                {
                    PlayerId = r.ReadInt32(),
                    Allowed = r.ReadBoolean(),
                    SessionTick = r.ReadUInt32(),
                    ErrorMessage = r.ReadString()
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
