using System.Collections.Generic;

namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Guest -> host: "my world is loaded and scanned, send me the current world
    /// state". The id hash is diagnostic only (logged on mismatch, never refused —
    /// scans grow over time, so a transient mismatch is normal).
    /// </summary>
    public sealed class WorldSnapshotRequest : IMessage
    {
        public uint IdHash;

        public MessageId Id => MessageId.WorldSnapshotRequest;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(IdHash);
        }

        public void Read(NetReader reader)
        {
            IdHash = reader.ReadUInt32();
        }
    }

    /// <summary>
    /// Host -> guest: last known synced state of doors the host has seen change
    /// (its own clicks and replicated remote ones). Untouched doors are not sent —
    /// both machines load them from the save. Chunked by the sender.
    /// </summary>
    public sealed class WorldDoorSnapshot : IMessage
    {
        /// <summary>Matches the sender's chunk size so malformed packets cannot allocate arbitrary lists.</summary>
        public const int MaxEntries = 60;

        public struct Entry
        {
            public uint NetId;
            public string StateName;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldDoorSnapshot;

        public void Write(NetWriter writer)
        {
            if (Entries.Count > MaxEntries)
                throw new ProtocolException($"World door snapshot has {Entries.Count} entries; limit is {MaxEntries}.");

            writer.WriteCount16(Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteString(entry.StateName);
            }
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new ProtocolException($"World door snapshot has {count} entries; limit is {MaxEntries}.");

            Entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Entries.Add(new Entry
                {
                    NetId = reader.ReadUInt32(),
                    StateName = reader.ReadString(),
                });
            }
        }
    }

    /// <summary>
    /// Host -> guest: at-rest poses of every registered item and vehicle, so a
    /// joiner sees the world where the host's session left it. Entries whose id
    /// the guest has not scanned yet are parked and applied on registration.
    /// Chunked by the sender.
    /// </summary>
    public sealed class WorldItemSnapshot : IMessage
    {
        /// <summary>Matches the sender's chunk size so malformed packets cannot allocate arbitrary lists.</summary>
        public const int MaxEntries = 40;

        public struct Entry
        {
            public uint ItemId;
            public NetVector3 Position;
            public NetQuaternion Rotation;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldItemSnapshot;

        public void Write(NetWriter writer)
        {
            if (Entries.Count > MaxEntries)
                throw new ProtocolException($"World item snapshot has {Entries.Count} entries; limit is {MaxEntries}.");

            writer.WriteCount16(Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.ItemId);
                writer.WriteVector3(entry.Position);
                writer.WriteQuaternion(entry.Rotation);
            }
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new ProtocolException($"World item snapshot has {count} entries; limit is {MaxEntries}.");

            Entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Entries.Add(new Entry
                {
                    ItemId = reader.ReadUInt32(),
                    Position = reader.ReadVector3(),
                    Rotation = reader.ReadQuaternion(),
                });
            }
        }
    }

    /// <summary>
    /// Host -> guest: every ready fitted bolt, including zero tightness. Guests restore
    /// its native array entry, parent total and scaled visual pose.
    /// Chunked by the sender.
    /// </summary>
    public sealed class WorldBoltSnapshot : IMessage
    {
        /// <summary>Matches the sender's chunk size so malformed packets cannot allocate arbitrary lists.</summary>
        public const int MaxEntries = 80;

        public struct Entry
        {
            public uint NetId;
            public ushort BoltTightness;
            public ushort ScrewInt;
            public float PartTightness;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldBoltSnapshot;

        public void Write(NetWriter writer)
        {
            if (Entries.Count > MaxEntries)
                throw new ProtocolException($"World bolt snapshot has {Entries.Count} entries; limit is {MaxEntries}.");

            writer.WriteCount16(Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteUInt16(entry.BoltTightness);
                writer.WriteUInt16(entry.ScrewInt);
            }
            foreach (var entry in Entries) writer.WriteSingle(entry.PartTightness);
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new ProtocolException($"World bolt snapshot has {count} entries; limit is {MaxEntries}.");

            Entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Entries.Add(new Entry
                {
                    NetId = reader.ReadUInt32(),
                    BoltTightness = reader.ReadUInt16(),
                    ScrewInt = reader.ReadUInt16(),
                });
            }
            for (int i = 0; i < count; i++)
            {
                var entry = Entries[i]; entry.PartTightness = reader.ReadSingle(); Entries[i] = entry;
            }
        }
    }

    /// <summary>
    /// Host -> guest: Installed/Tightness/Wear for every readable car part, including
    /// zero values. Guests write the variables on the part Data FSM.
    /// Chunked by the sender.
    /// </summary>
    public sealed class WorldPartSnapshot : IMessage
    {
        /// <summary>Matches the sender's chunk size so malformed packets cannot allocate arbitrary lists.</summary>
        public const int MaxEntries = 80;

        public struct Entry
        {
            public uint NetId;
            public byte Flags;
            public byte Tightness;
            public byte Wear;
            public float TightnessValue;
            public float WearValue;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldPartSnapshot;

        public void Write(NetWriter writer)
        {
            if (Entries.Count > MaxEntries)
                throw new ProtocolException($"World part snapshot has {Entries.Count} entries; limit is {MaxEntries}.");

            writer.WriteCount16(Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteByte(entry.Flags);
                writer.WriteByte(entry.Tightness);
                writer.WriteByte(entry.Wear);
            }
            // v107 appends full native scalars after the entire legacy entry block.
            foreach (var entry in Entries)
            {
                writer.WriteSingle(entry.TightnessValue);
                writer.WriteSingle(entry.WearValue);
            }
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new ProtocolException($"World part snapshot has {count} entries; limit is {MaxEntries}.");

            Entries = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                Entries.Add(new Entry
                {
                    NetId = reader.ReadUInt32(),
                    Flags = reader.ReadByte(),
                    Tightness = reader.ReadByte(),
                    Wear = reader.ReadByte(),
                });
            }
            for (int i = 0; i < count; i++)
            {
                var entry = Entries[i];
                entry.TightnessValue = reader.ReadSingle();
                entry.WearValue = reader.ReadSingle();
                Entries[i] = entry;
            }
        }
    }

    /// <summary>
    /// Host -> guest: item ids consumed/destroyed during this session so a mid-session
    /// joiner does not resurrect food and drinks the host already ate.
    /// </summary>
    public sealed class WorldItemDespawnSnapshot : IMessage
    {
        /// <summary>Matches the sender's chunk size so malformed packets cannot allocate arbitrary lists.</summary>
        public const int MaxItems = 80;

        public List<uint> ItemIds = new List<uint>();

        public MessageId Id => MessageId.WorldItemDespawnSnapshot;

        public void Write(NetWriter writer)
        {
            if (ItemIds.Count > MaxItems)
                throw new ProtocolException($"World item-despawn snapshot has {ItemIds.Count} ids; limit is {MaxItems}.");

            writer.WriteCount16(ItemIds.Count);
            foreach (uint itemId in ItemIds)
                writer.WriteUInt32(itemId);
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxItems)
                throw new ProtocolException($"World item-despawn snapshot has {count} ids; limit is {MaxItems}.");

            ItemIds = new List<uint>(count);
            for (int i = 0; i < count; i++)
                ItemIds.Add(reader.ReadUInt32());
        }
    }
}
