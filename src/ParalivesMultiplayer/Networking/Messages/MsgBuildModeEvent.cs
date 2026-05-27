using System.IO;
using System.Text;
using ParalivesMultiplayer.Networking;
using UnityEngine;

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
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
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
            msg.Position = input.ReadVector3();
            msg.Rotation = input.ReadQuaternion();
            msg.Scale = input.ReadVector3();
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
