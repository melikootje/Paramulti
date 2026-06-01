using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    // Host-authoritative ParaTime sync. Broadcast ~1 Hz by the host; clients snap their local
    // ParaTime to the host's TotalMinutes + speed + pause state on receive.
    public class MsgTimeSync : MessageBase
    {
        const string _code = "TimeSync";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public float TotalMinutes;
        public int TimeSpeedIndex;
        public bool IsPausedByPlayer;
        public bool IsPausedByUI;

        public override void Encode(BinaryWriter output)
        {
            output.Write(TotalMinutes);
            output.Write(TimeSpeedIndex);
            output.Write(IsPausedByPlayer);
            output.Write(IsPausedByUI);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgTimeSync();
            msg.TotalMinutes = input.ReadSingle();
            msg.TimeSpeedIndex = input.ReadInt32();
            msg.IsPausedByPlayer = input.ReadBoolean();
            msg.IsPausedByUI = input.ReadBoolean();
            message = msg;
            return true;
        }
    }
}
