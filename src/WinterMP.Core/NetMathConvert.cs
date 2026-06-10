using UnityEngine;
using WinterMP.Net;

namespace WinterMP.Core
{
    internal static class NetMathConvert
    {
        public static NetVector3 ToNet(this Vector3 v) => new NetVector3(v.x, v.y, v.z);
        public static Vector3 ToUnity(this NetVector3 v) => new Vector3(v.X, v.Y, v.Z);

        public static NetQuaternion ToNet(this Quaternion q) => new NetQuaternion(q.x, q.y, q.z, q.w);
        public static Quaternion ToUnity(this NetQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
    }
}
