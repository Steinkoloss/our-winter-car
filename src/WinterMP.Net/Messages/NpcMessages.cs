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
        /// <summary>The streamed animal has died (moose collision → corpse); appended v77.</summary>
        public const byte FlagDead = 2;

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

    /// <summary>
    /// Guest → host (v82): the sender's local copy of a streamed animal died (its car hit
    /// the moose, whose CarHit FSM is deliberately left live on guests). The host validates
    /// the reporter is near its own copy, then replays the vanilla death entry so the kill
    /// becomes authoritative and <see cref="NpcTransform.FlagDead"/> streams to everyone.
    /// Idempotent on the host (an already-dead mover ignores it), so the guest re-sends
    /// every few seconds until it sees FlagDead echoed — no ack latch to lose.
    /// </summary>
    public sealed class NpcDeathReport : IMessage
    {
        public uint NetId;
        public byte PlayerId;
        public ushort Sequence;

        public MessageId Id => MessageId.NpcDeathReport;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }
}
