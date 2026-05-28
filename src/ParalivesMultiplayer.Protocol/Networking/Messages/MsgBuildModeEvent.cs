using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;

namespace ParalivesMultiplayer.Networking.Messages
{
    public class MsgBuildModeEvent : MessageBase
    {
        const string _code = "BuildEvt";
        static readonly byte[] _codeBytes = Encoding.UTF8.GetBytes(_code);

        public override string MessageCode => _code;
        public override byte[] MessageCodeBytes => _codeBytes;

        public int PlayerId;
        public uint Tick;
        public BuildEventType EventType;
        public uint EntityId;
        public string ObjectTypeId;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public NetVector3 Scale;
        public string StyleName;

        public override void Encode(BinaryWriter output)
        {
            output.Write(PlayerId);
            output.Write(Tick);
            output.Write((int)EventType);
            output.Write(EntityId);
            output.Write(ObjectTypeId);
            output.Write(Position);
            output.Write(Rotation);
            output.Write(Scale);
            output.Write(StyleName);
        }

        public override bool TryDecode(BinaryReader input, out MessageBase message)
        {
            var msg = new MsgBuildModeEvent();
            msg.PlayerId = input.ReadInt32();
            msg.Tick = input.ReadUInt32();
            msg.EventType = (BuildEventType)input.ReadInt32();
            msg.EntityId = input.ReadUInt32();
            msg.ObjectTypeId = input.ReadString();
            msg.Position = input.ReadNetVector3();
            msg.Rotation = input.ReadNetQuaternion();
            msg.Scale = input.ReadNetVector3();
            msg.StyleName = input.ReadString();
            message = msg;
            return true;
        }
    }

    public enum BuildEventType
    {
        ObjectPlaced,
        ObjectRemoved,
        ObjectMoved,
        ObjectRotated,
        ObjectScaled,
        ObjectStyled,
        ModeEntered,
        ModeExited,
        SelectionChanged,
        Undo,
        Redo
    }
}
