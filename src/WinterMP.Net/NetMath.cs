namespace WinterMP.Net
{
    /// <summary>Engine-independent vector; converted to UnityEngine.Vector3 at the Core boundary.</summary>
    public struct NetVector3
    {
        public float X, Y, Z;

        public NetVector3(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }

        public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
    }

    /// <summary>Engine-independent quaternion; converted to UnityEngine.Quaternion at the Core boundary.</summary>
    public struct NetQuaternion
    {
        public float X, Y, Z, W;

        public NetQuaternion(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        public static NetQuaternion Identity => new NetQuaternion(0f, 0f, 0f, 1f);
    }
}
