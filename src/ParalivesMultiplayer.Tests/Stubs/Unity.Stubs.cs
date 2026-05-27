using System;

namespace UnityEngine
{
    public class MonoBehaviour { }

    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);

        public float this[int index]
        {
            get
            {
                if (index == 0) return x;
                if (index == 1) return y;
                return z;
            }
        }

        public static float Distance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

        public override bool Equals(object obj) => obj is Vector3 && this == (Vector3)obj;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public override string ToString() => $"({x}, {y}, {z})";
    }

    public struct Quaternion
    {
        public float x, y, z, w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new Quaternion(0, 0, 0, 1);

        public static float Angle(Quaternion a, Quaternion b)
        {
            float cos = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            cos = Mathf.Clamp(cos, -1f, 1f);
            return Mathf.Acos(Mathf.Abs(cos)) * 2f * 57.29577951308232f;
        }

        public static bool operator ==(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);

        public override bool Equals(object obj) => obj is Quaternion && this == (Quaternion)obj;
        public override int GetHashCode() => HashCode.Combine(x, y, z, w);
        public override string ToString() => $"({x}, {y}, {z}, {w})";
    }

    public static class Mathf
    {
        public static float Sqrt(float value) => (float)System.Math.Sqrt(value);
        public static float Abs(float value) => (value < 0) ? -value : value;
        public static float Clamp(float value, float min, float max) => (value < min) ? min : (value > max) ? max : value;
        public static float Acos(float value) => (float)System.Math.Acos(value);
        public static float PI => (float)System.Math.PI;
    }

    public class Time
    {
        public static float time = 0f;
    }
}
