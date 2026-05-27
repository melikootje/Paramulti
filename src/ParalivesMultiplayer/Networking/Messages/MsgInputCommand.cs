using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgInputCommand : MessageBase
    {
        const string _code = "InputCmd";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public uint Tick;
        public InputAction Action;
        public float ValueX;
        public float ValueY;
        public float ValueZ;
        public bool IsButton;
        public string ButtonName;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Tick);
            output.Write((int)Action);
            output.Write(ValueX);
            output.Write(ValueY);
            output.Write(ValueZ);
            output.Write(IsButton);
            output.Write(ButtonName);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgInputCommand();
            msg.PlayerId = input.ReadInt32();
            msg.Tick = input.ReadUInt32();
            msg.Action = (InputAction)input.ReadInt32();
            msg.ValueX = input.ReadSingle();
            msg.ValueY = input.ReadSingle();
            msg.ValueZ = input.ReadSingle();
            msg.IsButton = input.ReadBoolean();
            msg.ButtonName = input.ReadString();
            message = msg;
            return true;
        }
    }

    public enum InputAction
    {
        MoveHorizontal,
        MoveVertical,
        LookX,
        LookY,
        Jump,
        Sprint,
        Interact,
        BuildPlace,
        BuildDestroy,
        BuildRotate,
        BuildScaleUp,
        BuildScaleDown,
        Custom0,
        Custom1,
        Custom2
    }
}
