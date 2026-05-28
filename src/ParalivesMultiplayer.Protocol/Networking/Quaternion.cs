namespace ParalivesMultiplayer.Networking
{
    public struct NetQuaternion
    {
        public float x, y, z, w;

        public NetQuaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static NetQuaternion identity => new NetQuaternion(0, 0, 0, 1);

        public override string ToString() => $"({x}, {y}, {z}, {w})";
    }
}