using System.IO;

namespace ParalivesMultiplayer.Networking
{
    public static class BinarySerializationEx
    {
        public static void Write(this BinaryWriter writer, NetVector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        public static NetVector3 ReadNetVector3(this BinaryReader reader)
        {
            return new NetVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static void Write(this BinaryWriter writer, NetQuaternion q)
        {
            writer.Write(q.x);
            writer.Write(q.y);
            writer.Write(q.z);
            writer.Write(q.w);
        }

        public static NetQuaternion ReadNetQuaternion(this BinaryReader reader)
        {
            return new NetQuaternion(reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle());
        }
    }
}