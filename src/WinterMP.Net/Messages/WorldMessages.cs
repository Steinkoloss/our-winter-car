using System.Collections.Generic;

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
    /// The authoritative scalar setting of one fixed radiator thermostat. FSM state
    /// alone is insufficient here: an Increase/Decrease transition carries a delta,
    /// while a joining guest needs the resulting Rotation value.
    /// </summary>
    public sealed class RadiatorThermostatState : IMessage
    {
        /// <summary>Stable id of the thermostat Knob FSM.</summary>
        public uint NetId;
        /// <summary>Its game-owned <c>Rotation</c> float after the host applies a turn.</summary>
        public float Rotation;

        public MessageId Id => MessageId.RadiatorThermostatState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteSingle(Rotation);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            Rotation = reader.ReadSingle();
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

        /// <summary>
        /// Packet carries the sender's rigidbody velocity (moving vehicles). Receivers
        /// dead-reckon between packets so a fast car doesn't trail its true pose, and
        /// seed physics with it when a stream dies mid-drive.
        /// </summary>
        public const byte FlagHasVelocity = 8;

        public uint ItemId;
        /// <summary>Session player id of the peer simulating this item right now.</summary>
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public NetVector3 Position;
        public NetQuaternion Rotation = NetQuaternion.Identity;
        /// <summary>Only on the wire when <see cref="FlagHasVelocity"/> is set.</summary>
        public NetVector3 Velocity;

        public bool IsFinal => (Flags & FlagFinal) != 0;
        public bool IsDriver => (Flags & FlagDriver) != 0;
        public bool IsVehicle => (Flags & FlagVehicle) != 0;
        public bool HasVelocity => (Flags & FlagHasVelocity) != 0;

        public MessageId Id => MessageId.ItemTransform;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ItemId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            if (HasVelocity)
                writer.WriteVector3(Velocity);
        }

        public void Read(NetReader reader)
        {
            ItemId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            if (HasVelocity)
                Velocity = reader.ReadVector3();
        }
    }

    /// <summary>
    /// A fuel-station nozzle has increased a non-owned vehicle's local tank. The
    /// host verifies the player, nozzle and vehicle are co-located before accepting
    /// the bounded increase and sending a normal <see cref="VehicleState"/> back.
    /// </summary>
    public sealed class VehicleFuelIntent : IMessage
    {
        public uint VehicleId;
        public uint NozzleNetId;
        public byte PlayerId;
        public ushort Sequence;
        /// <summary>Requested tank level normalized to the target tank's capacity.</summary>
        public byte TargetFuelLevel;

        public MessageId Id => MessageId.VehicleFuelIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(VehicleId);
            writer.WriteUInt32(NozzleNetId);
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(TargetFuelLevel);
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            NozzleNetId = reader.ReadUInt32();
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            TargetFuelLevel = reader.ReadByte();
        }
    }

    /// <summary>
    /// Live vehicle-local poses of loose items riding a moving vehicle, streamed by
    /// the vehicle's transform owner alongside the vehicle stream. The owner's own
    /// physics simulates the cargo (sliding, rolling, tumbling — the fun part);
    /// receivers pin listed items kinematically and compose the streamed local pose
    /// against their own smoothed vehicle transform, so cargo tracks the car with
    /// zero world-space lag and replays the authority's in-car physics.
    ///
    /// Each packet is the COMPLETE cargo set for that vehicle: a tracked item that
    /// stops being listed is released back to local physics (seeded with the
    /// vehicle's velocity). The empty-set transition packet is sent reliably; the
    /// rest of the stream is best-effort.
    /// </summary>
    public sealed class VehicleCargo : IMessage
    {
        /// <summary>Hard cap on entries per packet; senders demote the farthest
        /// items back to ordinary world-space streams when exceeded.</summary>
        public const int MaxEntries = 24;

        private static readonly Entry[] NoEntries = new Entry[0];

        public struct Entry
        {
            public uint ItemId;
            /// <summary>Pose in the vehicle root rigidbody's local space.</summary>
            public NetVector3 LocalPosition;
            public NetQuaternion LocalRotation;
        }

        public uint VehicleId;
        /// <summary>Session player id of the peer streaming the vehicle (and its cargo).</summary>
        public byte OwnerPlayerId;
        public ushort Sequence;
        public Entry[] Entries = NoEntries;

        public MessageId Id => MessageId.VehicleCargo;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            int count = Entries.Length > MaxEntries ? MaxEntries : Entries.Length;
            writer.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
            {
                writer.WriteUInt32(Entries[i].ItemId);
                writer.WriteVector3(Entries[i].LocalPosition);
                writer.WriteQuaternion(Entries[i].LocalRotation);
            }
        }

        public void Read(NetReader reader)
        {
            VehicleId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            int count = reader.ReadByte();
            if (count > MaxEntries)
                throw new ProtocolException($"Vehicle cargo count {count} exceeds the {MaxEntries} entry limit.");

            Entries = count == 0 ? NoEntries : new Entry[count];
            for (int i = 0; i < count; i++)
            {
                Entries[i].ItemId = reader.ReadUInt32();
                Entries[i].LocalPosition = reader.ReadVector3();
                Entries[i].LocalRotation = reader.ReadQuaternion();
            }
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

        /// <summary>
        /// Host-only join/resync sentinel. It never changes a sender's live sequence
        /// baseline and cannot replace a local or guest-owned simulator. Live counters
        /// skip this value; normal sequence zero remains valid after wrap or admission.
        /// </summary>
        public const ushort SnapshotSequence = ushort.MaxValue;

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
        /// <summary>Selected gear + 1 (0 = reverse, 1 = neutral, 2.. = forward); appended v63.</summary>
        public byte Gear;

        /// <summary>Audited native drivetrain torque for host heating; appended v168.</summary>
        public bool TorqueAvailable;
        public float EngineTorque;
        /// <summary>Actual movement magnitude for cooling, separate from wheel speed; appended v169.</summary>
        public bool MovementSpeedAvailable;
        public ushort MovementSpeedTenthsKmh;
        /// <summary>Signed native differential angular speed, distinct from road speed; appended v202.</summary>
        public bool DifferentialSpeedAvailable;
        public float DifferentialSpeed;
        /// <summary>Exact signed native coolant for simulator handoff; appended v213.</summary>
        public bool HandoffTemperatureAvailable;
        public float HandoffTemperature;
        public uint FuelRevision;
        public float FuelLiters;
        public bool ValidFuelLiters => !float.IsNaN(FuelLiters) && !float.IsInfinity(FuelLiters)
            && FuelLiters >= 0 && (FuelRevision != 0 || FuelLiters == 0);
        public bool ValidHandoffTemperature => !float.IsNaN(HandoffTemperature) && !float.IsInfinity(HandoffTemperature)
            && HandoffTemperature >= -100 && HandoffTemperature <= 300
            && (HandoffTemperatureAvailable || HandoffTemperature == 0);
        public bool ValidDifferentialSpeed => !float.IsNaN(DifferentialSpeed) && !float.IsInfinity(DifferentialSpeed)
            && (DifferentialSpeedAvailable || DifferentialSpeed == 0);
        public bool ValidMovementSpeed => MovementSpeedAvailable || MovementSpeedTenthsKmh == 0;
        public bool ValidTorque => !float.IsNaN(EngineTorque) && !float.IsInfinity(EngineTorque)
            && (TorqueAvailable || EngineTorque == 0);

        public bool EngineOn => (Flags & FlagEngineOn) != 0;
        public bool AccOn => (Flags & FlagAccOn) != 0;
        public bool BlinkerLeft => (Flags & FlagBlinkerLeft) != 0;
        public bool BlinkerRight => (Flags & FlagBlinkerRight) != 0;
        public bool HazardOn => (Flags & FlagHazard) != 0;

        public MessageId Id => MessageId.VehicleState;

        public void Write(NetWriter writer)
        {
            if (!ValidTorque) throw new ProtocolException("Invalid vehicle torque.");
            if (!ValidMovementSpeed) throw new ProtocolException("Invalid vehicle movement speed.");
            if (!ValidDifferentialSpeed) throw new ProtocolException("Invalid vehicle differential speed.");
            if (!ValidHandoffTemperature) throw new ProtocolException("Invalid vehicle handoff temperature.");
            if (!ValidFuelLiters) throw new ProtocolException("Invalid vehicle absolute fuel.");
            writer.WriteUInt32(VehicleId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteUInt16(Rpm);
            writer.WriteUInt16(SpeedTenthsKmh);
            writer.WriteByte(FuelLevel);
            writer.WriteByte(CoolantTemp);
            writer.WriteByte(Gear);
            writer.WriteByte(TorqueAvailable ? (byte)1 : (byte)0);
            writer.WriteSingle(EngineTorque);
            writer.WriteByte(MovementSpeedAvailable ? (byte)1 : (byte)0);
            writer.WriteUInt16(MovementSpeedTenthsKmh);
            writer.WriteByte(DifferentialSpeedAvailable ? (byte)1 : (byte)0);
            writer.WriteSingle(DifferentialSpeed);
            writer.WriteByte(HandoffTemperatureAvailable ? (byte)1 : (byte)0);
            writer.WriteSingle(HandoffTemperature);
            writer.WriteUInt32(FuelRevision);
            writer.WriteSingle(FuelLiters);
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
            Gear = reader.ReadByte();
            byte torqueAvailable = reader.ReadByte();
            if (torqueAvailable > 1) throw new ProtocolException("Invalid vehicle torque availability.");
            TorqueAvailable = torqueAvailable == 1;
            EngineTorque = reader.ReadSingle();
            byte speedAvailable = reader.ReadByte();
            if (speedAvailable > 1) throw new ProtocolException("Invalid movement speed availability.");
            MovementSpeedAvailable = speedAvailable == 1;
            MovementSpeedTenthsKmh = reader.ReadUInt16();
            byte differentialAvailable = reader.ReadByte();
            if (differentialAvailable > 1) throw new ProtocolException("Invalid differential speed availability.");
            DifferentialSpeedAvailable = differentialAvailable == 1;
            DifferentialSpeed = reader.ReadSingle();
            byte temperatureAvailable = reader.ReadByte();
            if (temperatureAvailable > 1) throw new ProtocolException("Invalid handoff temperature availability.");
            HandoffTemperatureAvailable = temperatureAvailable == 1;
            HandoffTemperature = reader.ReadSingle();
            FuelRevision = reader.ReadUInt32();
            FuelLiters = reader.ReadSingle();
            if (!ValidFuelLiters) throw new ProtocolException("Invalid vehicle absolute fuel.");
            if (!ValidTorque) throw new ProtocolException("Invalid vehicle torque.");
            if (!ValidMovementSpeed) throw new ProtocolException("Invalid vehicle movement speed.");
            if (!ValidDifferentialSpeed) throw new ProtocolException("Invalid vehicle differential speed.");
            if (!ValidHandoffTemperature) throw new ProtocolException("Invalid vehicle handoff temperature.");
        }
    }

    /// <summary>
    /// Window condensation, exterior ice, and heater settings, streamed at ~2 Hz
    /// by the established vehicle owner (host fallback when unowned). Receivers write into
    /// the car's GlassFrosting / Freezing / HeaterUnit FSMs (and dashboard knob
    /// variables for the visible dial positions).
    /// </summary>
    public sealed class VehicleClimate : IMessage
    {
        public const byte FlagWindowHeater = 1;
        public const byte FlagGlassDefrosting = 2;
        /// <summary>Shared cabin occupancy; never writes native local entry or grants driving authority.</summary>
        public const byte FlagPlayerIn = 4;
        /// <summary>v190: CabinTemp has a finite native source; absent values must not supply heat.</summary>
        public const byte FlagCabinTemperature = 8;

        /// <summary>Sequence marking a join/resync snapshot; receivers apply it without dedup. See VehicleState.SnapshotSequence.</summary>
        public const ushort SnapshotSequence = ushort.MaxValue;

        public uint VehicleId;
        public byte OwnerPlayerId;
        public ushort Sequence;
        /// <summary>Interior glass frost (GlassFrosting.Frost), 0 = clear, 255 = fully frosted.
        /// Independent of <see cref="Ice"/> — a parked cold car can be iced outside yet clear
        /// inside, so these must not be collapsed into one value.</summary>
        public byte Frost;
        public byte Flags;
        /// <summary>Heater temp / blower / direction, each 0-255 (game-specific scale).</summary>
        public byte HeaterTemp;
        public byte HeaterBlower;
        public byte HeaterDirection;
        /// <summary>Retired v187: local capture sends 0; receivers ignore this byte.
        /// Native condensation uses Frost as _Color alpha, not white RGB tint or SweatRate.</summary>
        public byte Fog;
        /// <summary>Cabin air temperature, 0-255 maps to -40 to +40 °C.</summary>
        public byte CabinTemp;
        public bool HasCabinTemperature => (Flags & FlagCabinTemperature) != 0;

        public static bool TryQuantizeCabinTemperature(float celsius, out byte value)
        {
            value = 128;
            if (float.IsNaN(celsius) || float.IsInfinity(celsius)) return false;
            float unit = System.Math.Max(0f, System.Math.Min(1f, (celsius + 40f) / 80f));
            value = (byte)System.Math.Round(unit * 255f);
            return true;
        }

        public static float DequantizeCabinTemperature(byte value) => -40f + value / 255f * 80f;
        /// <summary>Exterior windshield cutoff, 0 = iced, 255 = clear. Independent of interior Frost.</summary>
        public byte Ice;
        // v184 keeps the original windshield byte and appends the other panes plus availability.
        public byte IceSideLeft, IceSideRight, IceDoorLeft, IceDoorRight, IceRear;
        public byte IceMask;
        /// <summary>Exact normalized parking lever setting from the established simulator; appended v221.</summary>
        public bool ParkingBrakeAvailable;
        public float ParkingBrake;
        public bool ValidParkingBrake => !float.IsNaN(ParkingBrake) && !float.IsInfinity(ParkingBrake)
            && ParkingBrake >= 0 && ParkingBrake <= 1 && (ParkingBrakeAvailable || ParkingBrake == 0);
        public const byte Windshield = 1, SideLeft = 2, SideRight = 4, DoorLeft = 8, DoorRight = 16, Rear = 32;
        public const byte AllWindows = 63;
        public bool ValidIce => (IceMask & ~AllWindows) == 0
            && ((IceMask & Windshield) != 0 || Ice == 0)
            && ((IceMask & SideLeft) != 0 || IceSideLeft == 0)
            && ((IceMask & SideRight) != 0 || IceSideRight == 0)
            && ((IceMask & DoorLeft) != 0 || IceDoorLeft == 0)
            && ((IceMask & DoorRight) != 0 || IceDoorRight == 0)
            && ((IceMask & Rear) != 0 || IceRear == 0);

        public static bool TryQuantizeIce(float cutoff, out byte value)
        {
            value = 0;
            if (float.IsNaN(cutoff) || float.IsInfinity(cutoff)) return false;
            value = (byte)System.Math.Round(System.Math.Max(0f, System.Math.Min(1f, cutoff)) * 255f);
            return true;
        }

        public static float DequantizeIce(byte value) => value / 255f;

        public bool WindowHeaterOn => (Flags & FlagWindowHeater) != 0;
        public bool GlassDefrosting => (Flags & FlagGlassDefrosting) != 0;
        public bool PlayerIn => (Flags & FlagPlayerIn) != 0;

        public MessageId Id => MessageId.VehicleClimate;

        public void Write(NetWriter writer)
        {
            if (!ValidIce) throw new ProtocolException("Invalid vehicle window ice.");
            if (!ValidParkingBrake) throw new ProtocolException("Invalid vehicle parking brake.");
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
            writer.WriteByte(Ice);
            writer.WriteByte(IceSideLeft); writer.WriteByte(IceSideRight);
            writer.WriteByte(IceDoorLeft); writer.WriteByte(IceDoorRight);
            writer.WriteByte(IceRear); writer.WriteByte(IceMask);
            writer.WriteByte(ParkingBrakeAvailable ? (byte)1 : (byte)0); writer.WriteSingle(ParkingBrake);
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
            Ice = reader.ReadByte();
            IceSideLeft = reader.ReadByte(); IceSideRight = reader.ReadByte();
            IceDoorLeft = reader.ReadByte(); IceDoorRight = reader.ReadByte();
            IceRear = reader.ReadByte(); IceMask = reader.ReadByte();
            byte parkingAvailable = reader.ReadByte();
            if (parkingAvailable > 1) throw new ProtocolException("Invalid parking brake availability.");
            ParkingBrakeAvailable = parkingAvailable == 1; ParkingBrake = reader.ReadSingle();
            if (!ValidIce) throw new ProtocolException("Invalid vehicle window ice.");
            if (!ValidParkingBrake) throw new ProtocolException("Invalid vehicle parking brake.");
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
        /// <summary>Legacy unit-range hint; v107 receivers use TightnessValue.</summary>
        public byte Tightness;
        /// <summary>Legacy unit-range hint; v107 receivers use WearValue (native wear is about 0-100).</summary>
        public byte Wear;
        public float TightnessValue;
        public float WearValue;

        public MessageId Id => MessageId.PartState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteByte(Flags);
            writer.WriteByte(Tightness);
            writer.WriteByte(Wear);
            writer.WriteSingle(TightnessValue);
            writer.WriteSingle(WearValue);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            Flags = reader.ReadByte();
            Tightness = reader.ReadByte();
            Wear = reader.ReadByte();
            TightnessValue = reader.ReadSingle();
            WearValue = reader.ReadSingle();
        }
    }

    /// <summary>
    /// Host result after a wrench turn settles: absolute bolt save-array value,
    /// visual pose and parent tightness. A guest report requests host correction.
    /// </summary>
    public sealed class BoltState : IMessage
    {
        public uint NetId;
        public ushort BoltTightness;
        public ushort ScrewInt; // Reserved zero since v114; a turn direction is not persistent state.
        public float PartTightness;

        public MessageId Id => MessageId.BoltState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(NetId);
            writer.WriteUInt16(BoltTightness);
            writer.WriteUInt16(ScrewInt);
            writer.WriteSingle(PartTightness);
        }

        public void Read(NetReader reader)
        {
            NetId = reader.ReadUInt32();
            BoltTightness = reader.ReadUInt16();
            ScrewInt = reader.ReadUInt16();
            PartTightness = reader.ReadSingle();
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
    /// Host-created item identities and template/pose descriptors. Receivers create
    /// views without running bag inventory actions. Sent on reliable ordered channel0;
    /// replays refresh live outputs and never revive retired identities.
    /// </summary>
    public sealed class ItemSpawn : IMessage
    {
        /// <summary>Per-packet limit; larger spills use additional manifest epochs.</summary>
        public const int MaxItems = 32;

        /// <summary>
        /// Wire v31: snapshot replay of an earlier spill (join and item resync in v105). The receiver must NOT
        /// fire its own container to materialize these (that would spend an unrelated,
        /// unopened local bag) — it adopts/instantiates from scene templates instead.
        /// Replays may recover missing live bodies; session-retired IDs never reappear.
        /// </summary>
        public const byte FlagReplay = 1;

        /// <summary>v106: catalog factory output. ContainerNetId identifies the factory;
        /// TemplateName carries the persistent native item ID, never a template search hint.</summary>
        public const byte FlagFactory = 2;

        public struct Entry
        {
            public uint NetId;
            /// <summary>Spawned object name (e.g. "potato chips(itemx)") — diagnostic / template hint.</summary>
            public string TemplateName;
            public NetVector3 Position;
            public NetQuaternion Rotation;
        }

        /// <summary>Persistent bag item ID, or catalog factory ID when FlagFactory is set.</summary>
        public uint ContainerNetId;
        /// <summary>Per-container spawn counter; lets guests dedup a re-delivered manifest.</summary>
        public ushort Epoch;
        /// <summary>The spiller — the peer whose live physics owns these until a final packet rests them.</summary>
        public byte OwnerPlayerId;
        /// <summary>Native spill state ("Spawn one"/"Spawn all") for diagnostics.</summary>
        public string StateName = string.Empty;
        public List<ItemSpawn.Entry> Items = new List<ItemSpawn.Entry>();
        /// <summary>Wire v31 (appended): bit 0 = <see cref="FlagReplay"/>.</summary>
        public byte Flags;
        /// <summary>
        /// Reserved legacy offer correlation (appended v32); zero for v120 bag output.
        /// </summary>
        public ushort OfferSequence;

        public bool IsReplay => (Flags & FlagReplay) != 0;
        public bool IsFactory => (Flags & FlagFactory) != 0;

        public MessageId Id => MessageId.ItemSpawn;

        public void Write(NetWriter writer)
        {
            if (Items.Count > MaxItems)
                throw new ProtocolException($"Item spawn manifest has {Items.Count} entries; limit is {MaxItems}.");

            writer.WriteUInt32(ContainerNetId);
            writer.WriteUInt16(Epoch);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteString(StateName);
            writer.WriteCount16(Items.Count);
            foreach (var entry in Items)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteString(entry.TemplateName);
                writer.WriteVector3(entry.Position);
                writer.WriteQuaternion(entry.Rotation);
            }
            writer.WriteByte(Flags);
            writer.WriteUInt16(OfferSequence);
        }

        public void Read(NetReader reader)
        {
            ContainerNetId = reader.ReadUInt32();
            Epoch = reader.ReadUInt16();
            OwnerPlayerId = reader.ReadByte();
            StateName = reader.ReadString();
            int count = reader.ReadUInt16();
            if (count > MaxItems)
                throw new ProtocolException($"Item spawn manifest has {count} entries; limit is {MaxItems}.");

            Items = new List<ItemSpawn.Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Items.Add(new ItemSpawn.Entry
                {
                    NetId = reader.ReadUInt32(),
                    TemplateName = reader.ReadString(),
                    Position = reader.ReadVector3(),
                    Rotation = reader.ReadQuaternion(),
                });
            }
            Flags = reader.ReadByte();
            OfferSequence = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// Retired v120. Codec retained for historical protocol diagnostics; the session
    /// dispatcher ignores these offers. BagOpenRequest now requests host inventory.
    /// </summary>
    public sealed class SpawnIntent : IMessage
    {
        /// <summary>Maximum clone descriptors a guest may offer from one grocery-bag spill.</summary>
        public const int MaxItems = 32;

        public struct Entry
        {
            /// <summary>Spilled clone name (e.g. "sausages(itemx)") — the host's template key.</summary>
            public string TemplateName;
            public NetVector3 Position;
            public NetQuaternion Rotation;
        }

        public byte PlayerId;
        public uint ContainerNetId;
        public string StateName = string.Empty;
        public ushort Sequence;
        /// <summary>Wire v32 (appended): the clones the guest's bag spilled, in capture order.</summary>
        public List<Entry> Items = new List<Entry>();

        public MessageId Id => MessageId.SpawnIntent;

        public void Write(NetWriter writer)
        {
            if (Items.Count > MaxItems)
                throw new ProtocolException($"Spawn intent has {Items.Count} entries; limit is {MaxItems}.");

            writer.WriteByte(PlayerId);
            writer.WriteUInt32(ContainerNetId);
            writer.WriteString(StateName);
            writer.WriteUInt16(Sequence);
            writer.WriteCount16(Items.Count);
            foreach (var entry in Items)
            {
                writer.WriteString(entry.TemplateName);
                writer.WriteVector3(entry.Position);
                writer.WriteQuaternion(entry.Rotation);
            }
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            ContainerNetId = reader.ReadUInt32();
            StateName = reader.ReadString();
            Sequence = reader.ReadUInt16();
            int count = reader.ReadUInt16();
            if (count > MaxItems)
                throw new ProtocolException($"Spawn intent has {count} entries; limit is {MaxItems}.");

            Items = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Items.Add(new Entry
                {
                    TemplateName = reader.ReadString(),
                    Position = reader.ReadVector3(),
                    Rotation = reader.ReadQuaternion(),
                });
            }
        }
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
        public const byte AllFlags = FlagWallet | FlagFsmStates | FlagParts | FlagBolts | FlagItems | FlagVehicles;

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
