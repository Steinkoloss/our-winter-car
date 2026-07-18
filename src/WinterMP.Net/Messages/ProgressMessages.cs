namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host-authoritative fluid amount carried by a tracked fuel or liquid container.
    /// The player currently simulating the item reports changes through the host; the
    /// host relays the accepted state to the other peers.
    /// </summary>
    public sealed class FluidContainerState : IMessage
    {
        public const byte FlagPouring = 1;

        public uint ItemId;
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public float Level;
        public float Capacity;

        public bool IsPouring => (Flags & FlagPouring) != 0;

        public MessageId Id => MessageId.FluidContainerState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ItemId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteSingle(Level);
            writer.WriteSingle(Capacity);
        }

        public void Read(NetReader reader)
        {
            ItemId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Level = reader.ReadSingle();
            Capacity = reader.ReadSingle();
        }
    }

    /// <summary>
    /// Compact host snapshot for a job or market subsystem. The meaning of the fields
    /// is fixed by <see cref="WorldProgressKind"/> and documented in PROTOCOL.md.
    /// </summary>
    public sealed class WorldProgressState : IMessage
    {
        public byte Kind;
        public byte Phase;
        public ushort Sequence;
        public int Primary;
        public int Secondary;
        public int Tertiary;
        public float Value;

        public MessageId Id => MessageId.WorldProgressState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Kind);
            writer.WriteByte(Phase);
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(Primary);
            writer.WriteInt32(Secondary);
            writer.WriteInt32(Tertiary);
            writer.WriteSingle(Value);
        }

        public void Read(NetReader reader)
        {
            Kind = reader.ReadByte();
            Phase = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Primary = reader.ReadInt32();
            Secondary = reader.ReadInt32();
            Tertiary = reader.ReadInt32();
            Value = reader.ReadSingle();
        }
    }

    /// <summary>Stable identifiers for the known host-mirrored work systems.</summary>
    public static class WorldProgressKind
    {
        public const byte Classifieds = 1;
        public const byte Factory = 2;
        public const byte MarkettiMagazine = 3;
    }

    /// <summary>
    /// Host-authoritative dynamic state for one delivery-job participant. Unlike the
    /// global work snapshots, there are several live customer sites and a shared
    /// sewage truck pump, so each is keyed by the stable hash of its FSM path.
    /// </summary>
    public sealed class JobSiteState : IMessage
    {
        public const byte KindSewage = 1;
        public const byte KindFirewood = 2;
        public const byte KindSewageTruck = 3;

        // Bits are kind-specific. For sites bit 0 means the order is active; for
        // the sewage truck it means the pump is running, with the other three bits
        // carrying the hose state needed to make an in-progress delivery readable.
        public const byte FlagActive = 1;
        public const byte FlagHoseAttached = 2;
        public const byte FlagHoseInWaste = 4;
        public const byte FlagSucking = 8;

        public uint SiteId;
        public byte Kind;
        public byte Flags;
        public ushort Sequence;
        public float Primary;
        public float Secondary;

        public bool IsActive => (Flags & FlagActive) != 0;

        public MessageId Id => MessageId.JobSiteState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(SiteId);
            writer.WriteByte(Kind);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(Primary);
            writer.WriteSingle(Secondary);
        }

        public void Read(NetReader reader)
        {
            SiteId = reader.ReadUInt32();
            Kind = reader.ReadByte();
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Primary = reader.ReadSingle();
            Secondary = reader.ReadSingle();
        }
    }

    /// <summary>
    /// Host-authoritative persisted state for an AMIS or Yellow Pages mail order.
    /// The three opaque data fields are the game's own saved package descriptor and
    /// must travel together; price alone cannot recreate the pending delivery.
    /// </summary>
    public sealed class MailOrderState : IMessage
    {
        public const byte KindAmis = 1;
        public const byte KindYellowPages = 2;
        public const byte KindHiddenYellowPages = 3;

        public const byte FlagActive = 1;
        public const byte FlagConsumed = 2;
        public const byte FlagSavePosition = 4;

        public byte Kind;
        public byte Flags;
        public ushort Sequence;
        public float Price;
        public float WaitTime;
        public int PriceInt;
        public string Data1 = string.Empty;
        public string Data2 = string.Empty;
        public string Data3 = string.Empty;

        public bool IsActive => (Flags & FlagActive) != 0;

        public MessageId Id => MessageId.MailOrderState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Kind);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(Price);
            writer.WriteSingle(WaitTime);
            writer.WriteInt32(PriceInt);
            writer.WriteString(Data1);
            writer.WriteString(Data2);
            writer.WriteString(Data3);
        }

        public void Read(NetReader reader)
        {
            Kind = reader.ReadByte();
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Price = reader.ReadSingle();
            WaitTime = reader.ReadSingle();
            PriceInt = reader.ReadInt32();
            Data1 = reader.ReadString();
            Data2 = reader.ReadString();
            Data3 = reader.ReadString();
        }
    }

    /// <summary>
    /// Guest request to pay for its locally-selected phone order. The order record
    /// is copied to the host immediately before the normal purchase intent, so the
    /// host runs the package spawn with the caller's actual generated listing.
    /// </summary>
    public sealed class MailOrderIntent : IMessage
    {
        public uint OrderNetId;
        public byte PlayerId;
        public byte Kind;
        public byte Flags;
        public ushort Sequence;
        public float Price;
        public float WaitTime;
        public int PriceInt;
        public string Data1 = string.Empty;
        public string Data2 = string.Empty;
        public string Data3 = string.Empty;

        public MessageId Id => MessageId.MailOrderIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(OrderNetId);
            writer.WriteByte(PlayerId);
            writer.WriteByte(Kind);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(Price);
            writer.WriteSingle(WaitTime);
            writer.WriteInt32(PriceInt);
            writer.WriteString(Data1);
            writer.WriteString(Data2);
            writer.WriteString(Data3);
        }

        public void Read(NetReader reader)
        {
            OrderNetId = reader.ReadUInt32();
            PlayerId = reader.ReadByte();
            Kind = reader.ReadByte();
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Price = reader.ReadSingle();
            WaitTime = reader.ReadSingle();
            PriceInt = reader.ReadInt32();
            Data1 = reader.ReadString();
            Data2 = reader.ReadString();
            Data3 = reader.ReadString();
        }
    }

    /// <summary>
    /// Host-authoritative inspection result and renewal record. The result masks
    /// retain the whole inspection checklist, not just the final pass bit, so the
    /// result sheet has the same failures on every peer.
    /// </summary>
    public sealed class InspectionState : IMessage
    {
        public const byte FlagCarInspected = 1;
        public const byte FlagStampOnPaper = 2;
        public const byte FlagMuseumRegistered = 4;
        public const byte FlagPassed = 8;
        public const byte FlagStampIssued = 16;
        public const byte FlagMuseumOrdered = 32;

        public byte Flags;
        public ushort Sequence;
        public int NextInspectionDay;
        public int InspectionIntervalDays;
        public int InspectionIntervalLetter;
        public uint ChecklistLow;
        public uint ChecklistHigh;
        /// <summary>v40: available physical registration-plate pairs (standard / museum).</summary>
        public byte PlateFlags;
        /// <summary>v40: host-generated standard registration plate text.</summary>
        public string StandardPlate = string.Empty;
        /// <summary>v40: host-generated museum registration plate text.</summary>
        public string MuseumPlate = string.Empty;

        public bool Passed => (Flags & FlagPassed) != 0;

        public MessageId Id => MessageId.InspectionState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(NextInspectionDay);
            writer.WriteInt32(InspectionIntervalDays);
            writer.WriteInt32(InspectionIntervalLetter);
            writer.WriteUInt32(ChecklistLow);
            writer.WriteUInt32(ChecklistHigh);
            writer.WriteByte(PlateFlags);
            writer.WriteString(StandardPlate);
            writer.WriteString(MuseumPlate);
        }

        public void Read(NetReader reader)
        {
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            NextInspectionDay = reader.ReadInt32();
            InspectionIntervalDays = reader.ReadInt32();
            InspectionIntervalLetter = reader.ReadInt32();
            ChecklistLow = reader.ReadUInt32();
            ChecklistHigh = reader.ReadUInt32();
            PlateFlags = reader.ReadByte();
            StandardPlate = reader.ReadString();
            MuseumPlate = reader.ReadString();
        }
    }

    /// <summary>
    /// A guest's locally observed checkpoint result. It is intentionally only a
    /// report: the host verifies its player pose, checkpoint and delegated car
    /// before it becomes a shared fine record.
    /// </summary>
    public sealed class PoliceIntent : IMessage
    {
        public const byte KnownOffenceMask = 0xFF;

        public byte PlayerId;
        public byte OffenceFlags;
        public ushort Sequence;
        public uint CheckpointId;
        public float Fine;

        public MessageId Id => MessageId.PoliceIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(OffenceFlags);
            writer.WriteUInt16(Sequence);
            writer.WriteUInt32(CheckpointId);
            writer.WriteSingle(Fine);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            OffenceFlags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            CheckpointId = reader.ReadUInt32();
            Fine = reader.ReadSingle();
        }
    }

    /// <summary>
    /// Host-approved police fine. The record feeds each peer's local Fines FSM;
    /// payment itself remains on the existing host-authoritative purchase path.
    /// </summary>
    public sealed class PoliceState : IMessage
    {
        public const byte FlagActive = 1;

        public byte PlayerId;
        public byte OffenceFlags;
        public byte Flags;
        public ushort Sequence;
        public uint CheckpointId;
        public float Fine;

        public bool IsActive => (Flags & FlagActive) != 0;

        public MessageId Id => MessageId.PoliceState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(OffenceFlags);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteUInt32(CheckpointId);
            writer.WriteSingle(Fine);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            OffenceFlags = reader.ReadByte();
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            CheckpointId = reader.ReadUInt32();
            Fine = reader.ReadSingle();
        }
    }

    /// <summary>Host-owned scalar settings for the fixed home radio/CD player.</summary>
    public sealed class HomeStereoState : IMessage
    {
        public const byte FlagRadioOn = 1;
        /// <summary>The game's raw <c>ChangeChannel.Channel</c> bool.</summary>
        public const byte FlagChannel = 2;

        public byte Flags;
        public ushort Sequence;
        public float Volume;
        public float Bass;

        public bool RadioOn => (Flags & FlagRadioOn) != 0;
        public bool Channel => (Flags & FlagChannel) != 0;

        public MessageId Id => MessageId.HomeStereoState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(Volume);
            writer.WriteSingle(Bass);
        }

        public void Read(NetReader reader)
        {
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Volume = reader.ReadSingle();
            Bass = reader.ReadSingle();
        }
    }

    /// <summary>Guest request for a nearby home stereo setting change.</summary>
    public sealed class HomeStereoIntent : IMessage
    {
        public byte PlayerId;
        public byte Flags;
        public ushort Sequence;
        public float Volume;
        public float Bass;

        public MessageId Id => MessageId.HomeStereoIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(Volume);
            writer.WriteSingle(Bass);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Volume = reader.ReadSingle();
            Bass = reader.ReadSingle();
        }
    }

    /// <summary>Guest report of entering a rally start line or numbered checkpoint.</summary>
    public sealed class RallyIntent : IMessage
    {
        public byte PlayerId;
        /// <summary>1-3 for SS1-SS3.</summary>
        public byte Stage;
        /// <summary>0 = start; 1-6 = numbered checkpoint.</summary>
        public byte Checkpoint;
        public ushort Sequence;

        public MessageId Id => MessageId.RallyIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Stage);
            writer.WriteByte(Checkpoint);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Stage = reader.ReadByte();
            Checkpoint = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }

    /// <summary>Host-timed rally progress used for observer and late-join convergence.</summary>
    public sealed class RallyState : IMessage
    {
        public const byte PhaseIdle = 0;
        public const byte PhaseRacing = 1;
        public const byte PhaseFinished = 2;

        public byte PlayerId;
        public byte Stage;
        public byte Phase;
        public byte Checkpoint;
        public ushort Sequence;
        /// <summary>Host elapsed time since valid start, in centiseconds.</summary>
        public uint ElapsedCentiseconds;

        public MessageId Id => MessageId.RallyState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Stage);
            writer.WriteByte(Phase);
            writer.WriteByte(Checkpoint);
            writer.WriteUInt16(Sequence);
            writer.WriteUInt32(ElapsedCentiseconds);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Stage = reader.ReadByte();
            Phase = reader.ReadByte();
            Checkpoint = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            ElapsedCentiseconds = reader.ReadUInt32();
        }
    }

    /// <summary>Guest report of a physical ice-track marker crossing.</summary>
    public sealed class IceRaceIntent : IMessage
    {
        public const byte MarkerStart = 0;
        public const byte MarkerCheckpoint1 = 1;
        public const byte MarkerCheckpoint2 = 2;
        public const byte MarkerFinish = 3;

        public byte PlayerId;
        public byte Marker;
        public ushort Sequence;

        public MessageId Id => MessageId.IceRaceIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Marker);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Marker = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }

    /// <summary>Host-timed position in one qualifying or lap-race run.</summary>
    public sealed class IceRaceState : IMessage
    {
        public const byte ModeTimeTrial = 1;
        public const byte ModeLapRace = 2;

        public byte PlayerId;
        public byte Mode;
        /// <summary>0 start/lap complete, 1 checkpoint one, 2 checkpoint two.</summary>
        public byte Checkpoint;
        public byte Laps;
        public ushort Sequence;
        public uint ElapsedCentiseconds;

        public MessageId Id => MessageId.IceRaceState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteByte(Mode);
            writer.WriteByte(Checkpoint);
            writer.WriteByte(Laps);
            writer.WriteUInt16(Sequence);
            writer.WriteUInt32(ElapsedCentiseconds);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Mode = reader.ReadByte();
            Checkpoint = reader.ReadByte();
            Laps = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            ElapsedCentiseconds = reader.ReadUInt32();
        }
    }

    /// <summary>Host-owned grid/heat variables for the ice-race event controller.</summary>
    public sealed class IceRaceEventState : IMessage
    {
        public const byte FlagGridReady = 1;
        public const byte FlagOnTrack = 2;
        public const byte FlagPlayerRegistered = 4;

        public byte Flags;
        public ushort Sequence;
        public int CarLimit;
        public int CarNumber;
        public int CarsOnTrack;
        public int HeatStage;
        public int Lane;
        public int RaceDistanceFinals;
        public int RaceDistanceQuals;
        public int StarterCars;
        public float Time;
        public string CarId = string.Empty;
        public string Reference = string.Empty;
        public int RaceStage;

        public MessageId Id => MessageId.IceRaceEventState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Flags);
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(CarLimit);
            writer.WriteInt32(CarNumber);
            writer.WriteInt32(CarsOnTrack);
            writer.WriteInt32(HeatStage);
            writer.WriteInt32(Lane);
            writer.WriteInt32(RaceDistanceFinals);
            writer.WriteInt32(RaceDistanceQuals);
            writer.WriteInt32(StarterCars);
            writer.WriteSingle(Time);
            writer.WriteString(CarId);
            writer.WriteString(Reference);
            writer.WriteInt32(RaceStage);
        }

        public void Read(NetReader reader)
        {
            Flags = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            CarLimit = reader.ReadInt32();
            CarNumber = reader.ReadInt32();
            CarsOnTrack = reader.ReadInt32();
            HeatStage = reader.ReadInt32();
            Lane = reader.ReadInt32();
            RaceDistanceFinals = reader.ReadInt32();
            RaceDistanceQuals = reader.ReadInt32();
            StarterCars = reader.ReadInt32();
            Time = reader.ReadSingle();
            CarId = reader.ReadString();
            Reference = reader.ReadString();
            RaceStage = reader.ReadInt32();
        }
    }

    /// <summary>Six ordered rows from the host ice-race result board.</summary>
    public sealed class IceRaceResultsState : IMessage
    {
        public const byte MaxRows = 6;

        public ushort Sequence;
        public string[] Names = new string[0];
        public string[] Numbers = new string[0];
        public string[] Models = new string[0];
        public string[] Uas = new string[0];

        public MessageId Id => MessageId.IceRaceResultsState;

        public void Write(NetWriter writer)
        {
            byte count = (byte)System.Math.Min(MaxRows, System.Math.Min(Names.Length,
                System.Math.Min(Numbers.Length, System.Math.Min(Models.Length, Uas.Length))));
            writer.WriteUInt16(Sequence);
            writer.WriteByte(count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteString(Names[i]);
                writer.WriteString(Numbers[i]);
                writer.WriteString(Models[i]);
                writer.WriteString(Uas[i]);
            }
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            byte count = reader.ReadByte();
            if (count > MaxRows) throw new ProtocolException("IceRaceResultsState row count exceeds limit.");
            Names = new string[count];
            Numbers = new string[count];
            Models = new string[count];
            Uas = new string[count];
            for (int i = 0; i < count; i++)
            {
                Names[i] = reader.ReadString();
                Numbers[i] = reader.ReadString();
                Models[i] = reader.ReadString();
                Uas[i] = reader.ReadString();
            }
        }
    }
}
