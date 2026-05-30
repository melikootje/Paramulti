using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgAnimationState : MessageBase
    {
        const string _code = "AnimationState";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public uint Tick;
        public int PlayerId;
        public int AnimatorStateHash;
        public float NormalizedTime;
        public float Speed;
        public bool IsInTransition;
        public int TransitionDestinationStateHash;

        public override void Encode(BinaryWriter output)
        {
            output.Write(Tick);
            output.Write(PlayerId);
            output.Write(AnimatorStateHash);
            output.Write(NormalizedTime);
            output.Write(Speed);
            output.Write(IsInTransition);
            output.Write(TransitionDestinationStateHash);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgAnimationState();
            msg.Tick = input.ReadUInt32();
            msg.PlayerId = input.ReadInt32();
            msg.AnimatorStateHash = input.ReadInt32();
            msg.NormalizedTime = input.ReadSingle();
            msg.Speed = input.ReadSingle();
            msg.IsInTransition = input.ReadBoolean();
            msg.TransitionDestinationStateHash = input.ReadInt32();
            message = msg;
            return true;
        }
    }
}
