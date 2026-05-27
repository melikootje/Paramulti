using System.IO;
using System.Text;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgSaveInitiate : MessageBase
    {
        const string _code = "SaveInit";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public string SceneName;
        public float TimeoutSeconds;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(SceneName);
            output.Write(TimeoutSeconds);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgSaveInitiate();
            msg.Tick = input.ReadUInt32();
            msg.SceneName = input.ReadString();
            msg.TimeoutSeconds = input.ReadSingle();
            message = msg;
            return true;
        }
    }

    public class MsgSaveAck : MessageBase
    {
        const string _code = "SaveAck";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public uint Tick;
        public bool Success;
        public string ErrorMessage;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Tick);
            output.Write(Success);
            output.Write(ErrorMessage);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgSaveAck();
            msg.PlayerId = input.ReadInt32();
            msg.Tick = input.ReadUInt32();
            msg.Success = input.ReadBoolean();
            msg.ErrorMessage = input.ReadString();
            message = msg;
            return true;
        }
    }

    public class MsgSaveComplete : MessageBase
    {
        const string _code = "SaveComp";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public bool AllAcksReceived;
        public int AcksReceived;
        public int TotalPlayers;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(AllAcksReceived);
            output.Write(AcksReceived);
            output.Write(TotalPlayers);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgSaveComplete();
            msg.Tick = input.ReadUInt32();
            msg.AllAcksReceived = input.ReadBoolean();
            msg.AcksReceived = input.ReadInt32();
            msg.TotalPlayers = input.ReadInt32();
            message = msg;
            return true;
        }
    }
}
