namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host-authoritative pose of an NPC/traffic rigidbody. Guests pin the body
    /// kinematic and ease toward streamed poses; the host simulates AI locally.
    /// </summary>
    public sealed class NpcTransform : IMessage
    {
        /// <summary>Last packet of a stream — object came to rest at this pose.</summary>
        public const byte FlagFinal = 1;

        public uint NetId;
        public ushort Sequence;
        public byte Flags;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;

        public bool IsFinal => (Flags & FlagFinal) != 0;

        public MessageId Id => MessageId.NpcTransform;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
        }
    }
}
