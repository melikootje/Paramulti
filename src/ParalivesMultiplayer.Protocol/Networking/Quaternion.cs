namespace ParalivesMultiplayer.Networking
{
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

        public override string ToString() => $"({x}, {y}, {z}, {w})";
    }
}
