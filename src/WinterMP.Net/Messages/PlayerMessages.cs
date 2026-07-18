namespace WinterMP.Net.Messages
{
    /// <summary>Broadcast by the host when a player joins (including a snapshot of players already present).</summary>
    public sealed class PlayerSpawn : IMessage
    {
        public byte PlayerId;
        public ulong SteamId;
        public string Name = string.Empty;

        public MessageId Id => MessageId.PlayerSpawn;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt64(SteamId);
            writer.WriteString(Name);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            SteamId = reader.ReadUInt64();
            Name = reader.ReadString();
        }
    }

    public sealed class PlayerDespawn : IMessage
    {
        public byte PlayerId;
        public string Reason = string.Empty;

        public MessageId Id => MessageId.PlayerDespawn;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteString(Reason);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Reason = reader.ReadString();
        }
    }

    /// <summary>
    /// Player avatar pose, streamed on <see cref="Channel.UnreliableSequenced"/>.
    /// Receivers drop packets whose <see cref="Sequence"/> is older than the last applied one.
    /// </summary>
    public sealed class PlayerTransform : IMessage
    {
        public byte PlayerId;
        public ushort Sequence;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        /// <summary>Coarse animation flags — see <c>WinterMP.Core.Sync.PlayerMoveState</c>.</summary>
        public byte MoveState;

        public MessageId Id => MessageId.PlayerTransform;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.WriteByte(MoveState);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            MoveState = reader.ReadByte();
        }
    }

    /// <summary>
    /// Host -> guest after the join snapshot: host pose plus optional last saved pose.
    /// Guest picks locally in-game (GuestSpawnPrompt).
    /// </summary>
    public sealed class GuestSpawn : IMessage
    {
        public const byte FlagHasLastPosition = 1;
        public const byte FlagHasSavedNeeds = 2;

        public NetVector3 HostPosition;
        public NetQuaternion HostRotation = NetQuaternion.Identity;
        public NetVector3 LastPosition;
        public NetQuaternion LastRotation = NetQuaternion.Identity;
        public byte Flags;
        public float Hunger;
        public float Fatigue;
        public float Thirst;
        public float Urine;
        /// <summary>Wire v28: saved BodyTemp (5th need), restored alongside the others.</summary>
        public float BodyTemp;
        /// <summary>Wire v30: saved Stress (global need float), restored on rejoin.</summary>
        public float Stress;
        /// <summary>Wire v30: saved drunkenness ("Drunk Mode" FSM DrunkCurrent), restored on rejoin.</summary>
        public float Drunk;

        public bool HasLastPosition => (Flags & FlagHasLastPosition) != 0;
        public bool HasSavedNeeds => (Flags & FlagHasSavedNeeds) != 0;

        public MessageId Id => MessageId.GuestSpawn;

        public void Write(NetWriter writer)
        {
            writer.WriteVector3(HostPosition);
            writer.WriteQuaternion(HostRotation);
            writer.WriteVector3(LastPosition);
            writer.WriteQuaternion(LastRotation);
            writer.WriteByte(Flags);
            writer.WriteSingle(Hunger);
            writer.WriteSingle(Fatigue);
            writer.WriteSingle(Thirst);
            writer.WriteSingle(Urine);
            writer.WriteSingle(BodyTemp);
            writer.WriteSingle(Stress);
            writer.WriteSingle(Drunk);
        }

        public void Read(NetReader reader)
        {
            HostPosition = reader.ReadVector3();
            HostRotation = reader.ReadQuaternion();
            LastPosition = reader.ReadVector3();
            LastRotation = reader.ReadQuaternion();
            Flags = reader.ReadByte();
            Hunger = reader.ReadSingle();
            Fatigue = reader.ReadSingle();
            Thirst = reader.ReadSingle();
            Urine = reader.ReadSingle();
            BodyTemp = reader.ReadSingle();
            Stress = reader.ReadSingle();
            Drunk = reader.ReadSingle();
        }
    }

    /// <summary>
    /// A player sat down in (or left) a passenger seat of a synced vehicle.
    /// Broadcast on enter/exit and re-sent every few seconds while seated so late
    /// joiners learn current occupancy. Receivers anchor that player's avatar to
    /// the seat and refuse local entry into it. Seat 255 = not seated.
    /// </summary>
    public sealed class PassengerState : IMessage
    {
        public const byte SeatNone = 255;

        public byte PlayerId;
        /// <summary>Synced item id of the vehicle (0 when <see cref="SeatIndex"/> is <see cref="SeatNone"/>).</summary>
        public uint VehicleId;
        /// <summary>0 = front passenger, 1 = rear left, 2 = rear right, 255 = none.</summary>
        public byte SeatIndex = SeatNone;

        public bool IsSeated => SeatIndex != SeatNone;

        public MessageId Id => MessageId.PassengerState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(SeatIndex);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            VehicleId = reader.ReadUInt32();
            SeatIndex = reader.ReadByte();
        }
    }
}
