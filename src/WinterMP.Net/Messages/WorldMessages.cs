namespace WinterMP.Net.Messages
{
    /// <summary>
    /// A cataloged PlayMaker FSM entered a synced state on the sender's machine
    /// (e.g. a door entered "Open door"). Receivers replay the entry through an
    /// injected global transition; see WorldSyncManager in WinterMP.Core.
    /// </summary>
    public sealed class FsmStateEnter : IMessage
    {
        /// <summary>Stable id of the FSM (scene-path hash, see PLAN §4.1).</summary>
        public uint NetId;
        public string StateName = string.Empty;

        public MessageId Id => MessageId.FsmStateEnter;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteString(StateName);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            StateName = reader.ReadString();
        }
    }

    /// <summary>
    /// A raw PlayMaker event to deliver to a cataloged FSM on the receiver
    /// (e.g. TIGHTEN/UNTIGHTEN on a bolt's Screw FSM). Receivers whitelist the
    /// event names they are willing to replay.
    /// </summary>
    public sealed class FsmRawEvent : IMessage
    {
        public uint NetId;
        public string EventName = string.Empty;

        public MessageId Id => MessageId.FsmRawEvent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteString(EventName);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            EventName = reader.ReadString();
        }
    }

    /// <summary>
    /// Pose of a world object (pickable item or vehicle root rigidbody) streamed by
    /// its current owner. Streamed on <see cref="Channel.UnreliableSequenced"/>
    /// while moving; the final at-rest pose is sent once with <see cref="FlagFinal"/>
    /// on <see cref="Channel.ReliableOrdered"/> so resting positions converge.
    /// </summary>
    public sealed class ItemTransform : IMessage
    {
        /// <summary>Set on the last packet of a stream: object came to rest at this pose.</summary>
        public const byte FlagFinal = 1;

        /// <summary>
        /// The sender's player is *driving* this vehicle. Driver claims beat
        /// proximity claims regardless of player id (see ownership rules).
        /// </summary>
        public const byte FlagDriver = 2;

        /// <summary>Transform stream is for a registered vehicle root rigidbody.</summary>
        public const byte FlagVehicle = 4;

        public uint ItemId;
        /// <summary>Session player id of the peer simulating this item right now.</summary>
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;

        public bool IsFinal => (Flags & FlagFinal) != 0;
        public bool IsDriver => (Flags & FlagDriver) != 0;
        public bool IsVehicle => (Flags & FlagVehicle) != 0;

        public MessageId Id => MessageId.ItemTransform;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ItemId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
        }

        public void Read(NetReader reader)
        {
            ItemId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
        }
    }

    /// <summary>
    /// Engine state of a vehicle, streamed by its owner at ~4 Hz while the engine
    /// turns. Receivers drive a synthetic engine audio source from it (the real
    /// engine simulation only runs on the owner's machine). Audio stops when no
    /// packet arrives for a couple of seconds.
    /// </summary>
    public sealed class VehicleState : IMessage
    {
        public const byte FlagEngineOn = 1;
        /// <summary>Key in ACC (dash electrics live, engine may be off).</summary>
        public const byte FlagAccOn = 2;
        public const byte FlagBlinkerLeft = 4;
        public const byte FlagBlinkerRight = 8;
        public const byte FlagHazard = 16;

        public uint VehicleId;
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public ushort Rpm;
        /// <summary>Speed in 0.1 km/h (e.g. 452 = 45.2 km/h) for remote gauge needles.</summary>
        public ushort SpeedTenthsKmh;
        /// <summary>Fuel gauge fill, 0 = empty, 255 = full.</summary>
        public byte FuelLevel;
        /// <summary>Coolant temp gauge, 0-255 maps to 0-120 °C.</summary>
        public byte CoolantTemp;

        public bool EngineOn => (Flags & FlagEngineOn) != 0;
        public bool AccOn => (Flags & FlagAccOn) != 0;
        public bool BlinkerLeft => (Flags & FlagBlinkerLeft) != 0;
        public bool BlinkerRight => (Flags & FlagBlinkerRight) != 0;
        public bool HazardOn => (Flags & FlagHazard) != 0;

        public MessageId Id => MessageId.VehicleState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Rpm);
            writer.WriteUInt16(SpeedTenthsKmh);
            writer.WriteByte(FuelLevel);
            writer.WriteByte(CoolantTemp);
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Rpm = reader.ReadUInt16();
            SpeedTenthsKmh = reader.ReadUInt16();
            FuelLevel = reader.ReadByte();
            CoolantTemp = reader.ReadByte();
        }
    }

    /// <summary>
    /// Window frost, interior fogging, and heater knob settings, streamed at ~2 Hz
    /// by whoever is near or driving the vehicle. Receivers write the values into
    /// the car's GlassFrosting / Freezing / HeaterUnit FSMs (and dashboard knob
    /// variables for the visible dial positions).
    /// </summary>
    public sealed class VehicleClimate : IMessage
    {
        public const byte FlagWindowHeater = 1;
        public const byte FlagGlassDefrosting = 2;
        /// <summary>Someone is in the cabin (drives interior sweat/fog sim).</summary>
        public const byte FlagPlayerIn = 4;

        public uint VehicleId;
        public byte OwnerPlayerId;
        public ushort Sequence;
        /// <summary>Exterior ice, 0 = clear, 255 = fully frosted.</summary>
        public byte Frost;
        public byte Flags;
        /// <summary>Heater temp / blower / direction, each 0-255 (game-specific scale).</summary>
        public byte HeaterTemp;
        public byte HeaterBlower;
        public byte HeaterDirection;
        /// <summary>Interior window fog / condensation, 0 = clear, 255 = fully fogged.</summary>
        public byte Fog;
        /// <summary>Cabin air temperature, 0-255 maps to 0-40 °C.</summary>
        public byte CabinTemp;

        public bool WindowHeaterOn => (Flags & FlagWindowHeater) != 0;
        public bool GlassDefrosting => (Flags & FlagGlassDefrosting) != 0;
        public bool PlayerIn => (Flags & FlagPlayerIn) != 0;

        public MessageId Id => MessageId.VehicleClimate;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Frost);
            writer.WriteByte(Flags);
            writer.WriteByte(HeaterTemp);
            writer.WriteByte(HeaterBlower);
            writer.WriteByte(HeaterDirection);
            writer.WriteByte(Fog);
            writer.WriteByte(CabinTemp);
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Frost = reader.ReadByte();
            Flags = reader.ReadByte();
            HeaterTemp = reader.ReadByte();
            HeaterBlower = reader.ReadByte();
            HeaterDirection = reader.ReadByte();
            Fog = reader.ReadByte();
            CabinTemp = reader.ReadByte();
        }
    }

    /// <summary>
    /// Authoritative car-part variables after install/bolt settles (Stop/Bolted/
    /// Unbolted). Receivers overwrite Installed/Tightness/Wear on the part Data FSM.
    /// </summary>
    public sealed class PartState : IMessage
    {
        public const byte FlagInstalled = 1;

        public uint NetId;
        public byte Flags;
        /// <summary>0-255 encoding of the part's Tightness float (0-1 range).</summary>
        public byte Tightness;
        /// <summary>0-255 encoding of the part's Wear float (0-1 range).</summary>
        public byte Wear;

        public MessageId Id => MessageId.PartState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteByte(Flags);
            writer.WriteByte(Tightness);
            writer.WriteByte(Wear);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            Flags = reader.ReadByte();
            Tightness = reader.ReadByte();
            Wear = reader.ReadByte();
        }
    }

    /// <summary>
    /// Authoritative bolt tightness after a wrench turn settles (Set pos). Receivers
    /// overwrite Screw FSM variables and replay Set pos for the visual.
    /// </summary>
    public sealed class BoltState : IMessage
    {
        public uint NetId;
        public ushort BoltTightness;
        public ushort ScrewInt;

        public MessageId Id => MessageId.BoltState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteUInt16(BoltTightness);
            writer.WriteUInt16(ScrewInt);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            BoltTightness = reader.ReadUInt16();
            ScrewInt = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// A tracked pickable was eaten or otherwise destroyed. Receivers remove their
    /// local copy of the rigidbody.
    /// </summary>
    public sealed class ItemDespawn : IMessage
    {
        public uint ItemId;

        public MessageId Id => MessageId.ItemDespawn;

        public void Write(NetWriter writer) => writer.WriteUInt32(ItemId);

        public void Read(NetReader reader) => ItemId = reader.ReadUInt32();
    }

    /// <summary>
    /// Host-authoritative game clock and weather, broadcast periodically and on
    /// join. Guests jump their sun/clock FSMs when drift exceeds a threshold and
    /// overwrite the weather forecast variables (host's forecast wins).
    /// </summary>
    public sealed class TimeSync : IMessage
    {
        /// <summary>Unknown / not yet probed.</summary>
        public const byte DayUnknown = 255;

        /// <summary>Game hour, 1-24 (the SUN clock FSM's own convention).</summary>
        public byte Hour;
        public float Minutes;
        public float TempOld;
        public float TempNew;
        public bool Snowing;
        public int ForecastIndex;
        /// <summary>Total in-game days elapsed (Systems/Statistics :: DaysPassed).</summary>
        public ushort DaysPassed;
        /// <summary>0 = Monday … 6 = Sunday; <see cref="DayUnknown"/> when unavailable.</summary>
        public byte DayOfWeek;

        public MessageId Id => MessageId.TimeSync;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Hour);
            writer.WriteSingle(Minutes);
            writer.WriteSingle(TempOld);
            writer.WriteSingle(TempNew);
            writer.WriteBool(Snowing);
            writer.WriteInt32(ForecastIndex);
            writer.WriteUInt16(DaysPassed);
            writer.WriteByte(DayOfWeek);
        }

        public void Read(NetReader reader)
        {
            Hour = reader.ReadByte();
            Minutes = reader.ReadSingle();
            TempOld = reader.ReadSingle();
            TempNew = reader.ReadSingle();
            Snowing = reader.ReadBool();
            ForecastIndex = reader.ReadInt32();
            DaysPassed = reader.ReadUInt16();
            DayOfWeek = reader.ReadByte();
        }
    }

    /// <summary>
    /// Host -> guests: lightweight checksum over wallet + synced world state (M7).
    /// Guests compare locally and may request a targeted resync on mismatch.
    /// </summary>
    public sealed class WorldStateChecksum : IMessage
    {
        public uint WalletCrc;
        public uint WorldCrc;
        public uint ItemCrc;
        public uint VehicleCrc;
        public ushort Sequence;

        public MessageId Id => MessageId.WorldStateChecksum;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(WalletCrc);
            writer.WriteUInt32(WorldCrc);
            writer.WriteUInt32(ItemCrc);
            writer.WriteUInt32(VehicleCrc);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            WalletCrc = reader.ReadUInt32();
            WorldCrc = reader.ReadUInt32();
            ItemCrc = reader.ReadUInt32();
            VehicleCrc = reader.ReadUInt32();
            Sequence = reader.ReadUInt16();
        }
    }

    /// <summary>Guest -> host: request authoritative state for a single registered net id.</summary>
    public sealed class WorldObjectStateRequest : IMessage
    {
        public uint NetId;

        public MessageId Id => MessageId.WorldObjectStateRequest;

        public void Write(NetWriter writer) => writer.WriteUInt32(NetId);

        public void Read(NetReader reader) => NetId = reader.ReadUInt32();
    }

    /// <summary>Guest -> host: soft resync of one or more state groups after checksum mismatch.</summary>
    public sealed class WorldResyncRequest : IMessage
    {
        public const byte FlagWallet = 1 << 0;
        public const byte FlagFsmStates = 1 << 1;
        public const byte FlagParts = 1 << 2;
        public const byte FlagBolts = 1 << 3;
        public const byte FlagItems = 1 << 4;
        public const byte FlagVehicles = 1 << 5;

        public byte Flags;
        public ushort ChecksumSequence;

        public MessageId Id => MessageId.WorldResyncRequest;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Flags);
            writer.WriteUInt16(ChecksumSequence);
        }

        public void Read(NetReader reader)
        {
            Flags = reader.ReadByte();
            ChecksumSequence = reader.ReadUInt16();
        }
    }
}
