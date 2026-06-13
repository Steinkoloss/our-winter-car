namespace WinterMP.Net.Messages
{
    /// <summary>Cause bytes for death reports (Systems/Death :: Activate Dead Body).</summary>
    public static class DeathCause
    {
        public const byte Unknown = 0;
        public const byte Fatigue = 1;
        public const byte Hunger = 2;
        public const byte Thirst = 3;
        public const byte Urine = 4;
        public const byte Stress = 5;
        public const byte RunOver = 6;
        public const byte Drown = 7;
        public const byte Fire = 8;
        public const byte Electrocute = 9;
        public const byte Hypothermia = 10;
        public const byte Murder = 11;
        public const byte Train = 12;
        public const byte Accident = 13;
    }

    public static class PlayerDeathEventFlags
    {
        /// <summary>Permadeath session — all clients should enter the death flow.</summary>
        public const byte PermadeathWipe = 1;
    }

    /// <summary>Any player -> host: local death sequence started.</summary>
    public sealed class PlayerDeathReport : IMessage
    {
        public byte PlayerId;
        public byte Cause;
        public byte Sequence;

        public MessageId Id => MessageId.PlayerDeathReport;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Cause);
            writer.WriteByte(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Cause = reader.ReadByte();
            Sequence = reader.ReadByte();
        }
    }

    /// <summary>Host -> all: a player died (or permadeath group wipe).</summary>
    public sealed class PlayerDeathEvent : IMessage
    {
        public byte PlayerId;
        public byte Cause;
        public byte Flags;

        public MessageId Id => MessageId.PlayerDeathEvent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Cause);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Cause = reader.ReadByte();
            Flags = reader.ReadByte();
        }
    }

    /// <summary>Guest/host -> all: player finished the non-permadeath respawn flow and is back in the world.</summary>
    public sealed class PlayerRespawn : IMessage
    {
        public byte PlayerId;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        public byte Sequence;

        public MessageId Id => MessageId.PlayerRespawn;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.WriteByte(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            Sequence = reader.ReadByte();
        }
    }
}
