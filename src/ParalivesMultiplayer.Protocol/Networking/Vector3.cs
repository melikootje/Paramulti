namespace ParalivesMultiplayer.Networking
{
    public struct NetVector3
    {
        public float x, y, z;

        public NetVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static NetVector3 zero => new NetVector3(0, 0, 0);
        public static NetVector3 one => new NetVector3(1, 1, 1);

        public override string ToString() => $"({x}, {y}, {z})";
    }
}