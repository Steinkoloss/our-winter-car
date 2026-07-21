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
        /// Sequence value marking a join/resync snapshot. Receivers apply it without
        /// the live-stream dedup, so a fresh guest (whose LastVehicleStateSequence is
        /// still 0) does not discard the Sequence-0 snapshot and miss a parked car's
        /// engine/electrics state. The live stream never emits this value as a real
        /// pose (at worst one un-deduped packet after a full ushort wrap, ~4.5 h).
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
            writer.WriteByte(Gear);
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
        /// <summary>Interior window fog / condensation, 0 = clear, 255 = fully fogged.</summary>
        public byte Fog;
        /// <summary>Cabin air temperature, 0-255 maps to -40 to +40 °C.</summary>
        public byte CabinTemp;
        /// <summary>Exterior window ice (Freezing.CutoffWindshield), 0 = clear, 255 = fully iced.
        /// Wire v28: split out from <see cref="Frost"/> so parked cars stop force-frosting the
        /// interior on observers.</summary>
        public byte Ice;

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
            writer.WriteByte(Ice);
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
    /// Host -> guests: authoritative manifest of items a container FSM just spawned
    /// (grocery-bag "Spawn all"/"Spawn one"). Runtime-`Instantiate`d clones exist in
    /// neither save, so peers cannot discover them by stable scene path; the host is
    /// the sole authority for their identity. The spiller (the peer whose player
    /// opened the bag — <see cref="OwnerPlayerId"/>) binds its own captured clones
    /// to these net ids; every other peer materializes matching objects from its own
    /// scene (adopt a nearby clone, else instantiate from a template) and holds them
    /// owner-followed until the owner's <see cref="ItemTransform"/> stream rests
    /// them. Nobody fires a bag FSM on receipt (v32; the bag's own player-interaction
    /// checks make remote firing impossible). Sent on <see cref="Channel.ReliableOrdered"/>.
    /// </summary>
    public sealed class ItemSpawn : IMessage
    {
        /// <summary>A grocery-bag spill cannot legitimately contain an unbounded number of clones.</summary>
        public const int MaxItems = SpawnIntent.MaxItems;

        /// <summary>
        /// Wire v31: join-snapshot replay of an earlier spill. The receiver must NOT
        /// fire its own container to materialize these (that would spend an unrelated,
        /// unopened local bag) — it adopts/instantiates from scene templates instead.
        /// </summary>
        public const byte FlagReplay = 1;

        public struct Entry
        {
            public uint NetId;
            /// <summary>Spawned object name (e.g. "potato chips(itemx)") — diagnostic / template hint.</summary>
            public string TemplateName;
            public NetVector3 Position;
            public NetQuaternion Rotation;
        }

        /// <summary>Net id of the spawning container FSM (the bag).</summary>
        public uint ContainerNetId;
        /// <summary>Per-container spawn counter; lets guests dedup a re-delivered manifest.</summary>
        public ushort Epoch;
        /// <summary>The spiller — the peer whose live physics owns these until a final packet rests them.</summary>
        public byte OwnerPlayerId;
        /// <summary>Container state that spilled ("Spawn one"/"Spawn all") — diagnostic and offer matching.</summary>
        public string StateName = string.Empty;
        public List<ItemSpawn.Entry> Items = new List<ItemSpawn.Entry>();
        /// <summary>Wire v31 (appended): bit 0 = <see cref="FlagReplay"/>.</summary>
        public byte Flags;
        /// <summary>
        /// Wire v32 (appended): echoes <see cref="SpawnIntent.Sequence"/> when this
        /// manifest answers a guest offer; 0 for host spills and replays. The offering
        /// guest pairs the answer to the exact offer with it — two quick "Spawn one"
        /// offers on the same bag are indistinguishable by (container, state) alone.
        /// </summary>
        public ushort OfferSequence;

        public bool IsReplay => (Flags & FlagReplay) != 0;

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
    /// Guest -> host: "my grocery bag just spilled these clones — mint their ids".
    /// The guest lets its bag run naturally (a bag FSM cannot be driven remotely:
    /// its "Confirm" state bounces back to "Wait player" without a live player
    /// interaction), captures the spilled clones, and offers their names + poses.
    /// The host materializes matching copies from its own scene templates, mints
    /// authoritative net ids, and answers with the <see cref="ItemSpawn"/> manifest
    /// every peer binds to (the offering guest binds its captured clones and keeps
    /// streaming them as owner). Items appended v32 — before that the guest aborted
    /// its spill and asked the host to re-fire its replica bag, which the FSM's own
    /// interaction checks made impossible.
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
