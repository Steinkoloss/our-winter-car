namespace WinterMP.Net.Messages
{
    /// <summary>Broadcast by the host when a player joins (including a snapshot of players already present).</summary>
    public sealed class PlayerSpawn : IMessage
    {
        public byte PlayerId;
        public ulong SteamId;
        public string Name = string.Empty;
        public ulong ClothingAdmission;

        public MessageId Id => MessageId.PlayerSpawn;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt64(SteamId);
            writer.WriteString(Name);
            writer.WriteUInt64(ClothingAdmission);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            SteamId = reader.ReadUInt64();
            Name = reader.ReadString();
            ClothingAdmission = reader.ReadUInt64();
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
        public bool HasSweat;
        public float Sweat;
        public bool ValidSweat => HasSweat ? PassengerCondensationPolicy.ValidSweat(Sweat) : Sweat == 0f;

        public MessageId Id => MessageId.PlayerTransform;

        public void Write(NetWriter writer)
        {
            if (!ValidSweat) throw new ProtocolException("Invalid player sweat.");
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.WriteByte(MoveState);
            writer.WriteByte(HasSweat ? (byte)1 : (byte)0);
            writer.WriteSingle(Sweat);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            MoveState = reader.ReadByte();
            byte available = reader.ReadByte();
            if (available > 1) throw new ProtocolException("Invalid player sweat availability.");
            HasSweat = available == 1;
            Sweat = reader.ReadSingle();
            if (!ValidSweat) throw new ProtocolException("Invalid player sweat.");
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
        /// <summary>Wire v55: the persisted dirtiness value is known (legacy sidecars omit it).</summary>
        public const byte FlagHasSavedDirtiness = 4;
        /// <summary>Wire v78: the persisted PlayerAlco BAC is known (legacy sidecars omit it).</summary>
        public const byte FlagHasSavedAlco = 8;
        /// <summary>v189: saved native PlayerTemp is known; legacy ambient samples are omitted.</summary>
        public const byte FlagHasSavedBodyTemp = 16;

        public NetVector3 HostPosition;
        public NetQuaternion HostRotation = NetQuaternion.Identity;
        public NetVector3 LastPosition;
        public NetQuaternion LastRotation = NetQuaternion.Identity;
        public byte Flags;
        public float Hunger;
        public float Fatigue;
        public float Thirst;
        public float Urine;
        /// <summary>v189: native PlayerTemp, applied only with FlagHasSavedBodyTemp.</summary>
        public float BodyTemp;
        /// <summary>Wire v30: saved Stress (global need float), restored on rejoin.</summary>
        public float Stress;
        /// <summary>Wire v30: saved drunkenness ("Drunk Mode" FSM DrunkCurrent), restored on rejoin.</summary>
        public float Drunk;
        /// <summary>Wire v55: saved PlayerDirtiness, restored only when the sidecar supplied it.</summary>
        public float Dirtiness;
        /// <summary>Wire v78: saved persistent PlayerAlco BAC, restored only when the sidecar supplied it.</summary>
        public float PlayerAlco;
        public byte ClothingPlayerId;
        public ulong ClothingAdmission;
        public bool HasSavedClothing;
        public byte ClothingStage, ClothingType, WinterGarment;
        public bool ValidClothing => HasSavedClothing ? WinterGarment <= 2
            : ClothingStage == 0 && ClothingType == 0 && WinterGarment == 0;

        public bool HasLastPosition => (Flags & FlagHasLastPosition) != 0;
        public bool HasSavedNeeds => (Flags & FlagHasSavedNeeds) != 0;
        public bool HasSavedDirtiness => (Flags & FlagHasSavedDirtiness) != 0;
        public bool HasSavedAlco => (Flags & FlagHasSavedAlco) != 0;
        public bool HasSavedBodyTemp => (Flags & FlagHasSavedBodyTemp) != 0;
        public bool ValidBodyTemp => (!HasSavedBodyTemp || HasSavedNeeds)
            && PlayerWarmthPolicy.Valid(HasSavedBodyTemp, BodyTemp);

        public MessageId Id => MessageId.GuestSpawn;

        public void Write(NetWriter writer)
        {
            if (!ValidBodyTemp) throw new ProtocolException("Invalid saved body warmth.");
            if (!ValidClothing) throw new ProtocolException("Invalid saved clothing.");
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
            writer.WriteSingle(Dirtiness);
            writer.WriteSingle(PlayerAlco);
            writer.WriteByte(ClothingPlayerId);
            writer.WriteUInt64(ClothingAdmission);
            writer.WriteBool(HasSavedClothing);
            writer.WriteByte(ClothingStage);
            writer.WriteByte(ClothingType);
            writer.WriteByte(WinterGarment);
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
            Dirtiness = reader.ReadSingle();
            PlayerAlco = reader.ReadSingle();
            ClothingPlayerId = reader.ReadByte();
            ClothingAdmission = reader.ReadUInt64();
            byte clothingAvailable = reader.ReadByte();
            if (clothingAvailable > 1) throw new ProtocolException("Invalid clothing availability.");
            HasSavedClothing = clothingAvailable == 1;
            ClothingStage = reader.ReadByte();
            ClothingType = reader.ReadByte();
            WinterGarment = reader.ReadByte();
            if (!ValidClothing) throw new ProtocolException("Invalid saved clothing.");
            if (!ValidBodyTemp) throw new ProtocolException("Invalid saved body warmth.");
        }
    }

    /// <summary>
    /// A player sat down in (or left) a passenger seat of a synced vehicle.
    /// Broadcast on enter/exit and re-sent every few seconds while seated so late
    /// joiners learn current occupancy. Receivers anchor that player's avatar to
    /// the seat and refuse local entry into it. Seat 255 = not seated. Sequence
    /// is append-only (v52) so the host and receivers can reject delayed/replayed
    /// reliable reports.
    /// </summary>
    public sealed class PassengerState : IMessage
    {
        public const byte SeatNone = 255;

        public byte PlayerId;
        /// <summary>Synced item id of the vehicle (0 when <see cref="SeatIndex"/> is <see cref="SeatNone"/>).</summary>
        public uint VehicleId;
        /// <summary>0 = front passenger, 1 = rear right (reserved in taxi), 2 = rear left, 255 = none.</summary>
        public byte SeatIndex = SeatNone;
        public ushort Sequence;

        public bool IsSeated => SeatIndex != SeatNone;

        public MessageId Id => MessageId.PassengerState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(SeatIndex);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            VehicleId = reader.ReadUInt32();
            SeatIndex = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }
}
