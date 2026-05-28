using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgUpdateState : MessageBase
    {
        const string _code = "UpdateState";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public int PlayerId;
        public NetVector3 Position;
        public NetVector3 Velocity;
        public NetQuaternion Rotation;
        public float InputHorizontal;
        public float InputVertical;
        public bool JumpPressed;
        public bool AttackPressed;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(PlayerId);
            output.Write(Position);
            output.Write(Velocity);
            output.Write(Rotation);
            output.Write(InputHorizontal);
            output.Write(InputVertical);
            output.Write(JumpPressed);
            output.Write(AttackPressed);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgUpdateState();
            msg.Tick = input.ReadUInt32();
            msg.PlayerId = input.ReadInt32();
            msg.Position = input.ReadNetVector3();
            msg.Velocity = input.ReadNetVector3();
            msg.Rotation = input.ReadNetQuaternion();
            msg.InputHorizontal = input.ReadSingle();
            msg.InputVertical = input.ReadSingle();
            msg.JumpPressed = input.ReadBoolean();
            msg.AttackPressed = input.ReadBoolean();
            message = msg;
            return true;
        }
    }
}
