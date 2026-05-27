using System;
using UnityEngine;

namespace ParalivesMultiplayer.Networking
{
    public static class BinarySerializationEx
    {
        public static void Write(this System.IO.BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        public static Vector3 ReadVector3(this System.IO.BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        public static void Write(this System.IO.BinaryWriter writer, Quaternion q)
        {
            writer.Write(q.x);
            writer.Write(q.y);
            writer.Write(q.z);
            writer.Write(q.w);
        }

        public static Quaternion ReadQuaternion(this System.IO.BinaryReader reader)
        {
            return new Quaternion(reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
