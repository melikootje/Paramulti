namespace ParalivesMultiplayer.Networking
{
    public static class UnityConversion
    {
        public static UnityEngine.Vector3 ToUnity(this Vector3 v)
            => new UnityEngine.Vector3(v.x, v.y, v.z);

        public static UnityEngine.Quaternion ToUnity(this Quaternion q)
            => new UnityEngine.Quaternion(q.x, q.y, q.z, q.w);

        public static Vector3 FromUnity(this UnityEngine.Vector3 v)
            => new Vector3(v.x, v.y, v.z);

        public static Quaternion FromUnity(this UnityEngine.Quaternion q)
            => new Quaternion(q.x, q.y, q.z, q.w);
    }
}
