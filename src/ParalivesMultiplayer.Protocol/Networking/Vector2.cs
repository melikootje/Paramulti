namespace ParalivesMultiplayer.Networking
{
    public struct NetVector2
    {
        public float x, y;

        public NetVector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static NetVector2 zero => new NetVector2(0, 0);

        public override string ToString() => $"({x}, {y})";
    }
}