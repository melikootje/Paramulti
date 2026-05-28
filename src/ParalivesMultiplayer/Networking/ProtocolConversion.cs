namespace ParalivesMultiplayer.Networking
{
    public static class UnityConversion
    {
        public static UnityEngine.Vector3 ToUnity(this NetVector3 v)
            => new UnityEngine.Vector3(v.x, v.y, v.z);

        public static UnityEngine.Quaternion ToUnity(this NetQuaternion q)
            => new UnityEngine.Quaternion(q.x, q.y, q.z, q.w);

        public static NetVector3 FromUnity(this UnityEngine.Vector3 v)
            => new NetVector3(v.x, v.y, v.z);

        public static NetQuaternion FromUnity(this UnityEngine.Quaternion q)
            => new NetQuaternion(q.x, q.y, q.z, q.w);
    }
}