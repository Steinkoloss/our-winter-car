using System;

namespace WinterMP.Net.Messages
{
    public enum PartParentKind : byte { None, NativePart, Vehicle }

    /// <summary>Native replacement identity and creation state, owned by the host.</summary>
    public sealed class ReplacementPartState : IMessage
    {
        public const int MaxScalars = 8;
        public uint Revision, FactoryId;
        public string NativeId = string.Empty;
        public int AssemblyId;
        public bool Installed; // AssemblyId > 0; never the native Data.Installed scratch value.
        public float[] Scalars = new float[0];
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public PartParentKind ParentKind;
        public uint ParentId;
        public string ParentPath = string.Empty;
        public NetVector3 LocalPosition;
        public NetQuaternion LocalRotation = NetQuaternion.Identity;
        public NetVector3 LocalScale = new NetVector3(1, 1, 1);
        public bool RemovalAllowed;
        public MessageId Id => MessageId.ReplacementPartState;
        public void Write(NetWriter w)
        {
            if (Scalars == null || Scalars.Length > MaxScalars) throw new ProtocolException("Invalid replacement scalar count.");
            w.WriteUInt32(Revision); w.WriteUInt32(FactoryId); w.WriteString(NativeId);
            w.WriteInt32(AssemblyId); w.WriteBool(Installed); w.WriteByte((byte)Scalars.Length);
            foreach (float value in Scalars) w.WriteSingle(value);
            w.WriteVector3(Position); w.WriteQuaternion(Rotation);
            w.WriteByte((byte)ParentKind); w.WriteUInt32(ParentId); w.WriteString(ParentPath);
            w.WriteVector3(LocalPosition); w.WriteQuaternion(LocalRotation); w.WriteVector3(LocalScale);
            w.WriteBool(RemovalAllowed);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); FactoryId = r.ReadUInt32(); NativeId = r.ReadString();
            AssemblyId = r.ReadInt32();
            byte installed = r.ReadByte();
            if (installed > 1) throw new ProtocolException("Invalid replacement installed flag.");
            Installed = installed != 0;
            int count = r.ReadByte();
            if (count > MaxScalars) throw new ProtocolException("Invalid replacement scalar count.");
            Scalars = new float[count];
            for (int i = 0; i < count; i++) Scalars[i] = r.ReadSingle();
            Position = r.ReadVector3(); Rotation = r.ReadQuaternion();
            ParentKind = (PartParentKind)r.ReadByte(); ParentId = r.ReadUInt32(); ParentPath = r.ReadString();
            LocalPosition = r.ReadVector3(); LocalRotation = r.ReadQuaternion(); LocalScale = r.ReadVector3();
            byte removal = r.ReadByte();
            if (removal > 1) throw new ProtocolException("Invalid replacement removal flag.");
            RemovalAllowed = removal != 0;
        }
    }
}
