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
        public struct Entry
        {
            public uint NetId;
            public string StateName;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldDoorSnapshot;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16((ushort)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteString(entry.StateName);
            }
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
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
            writer.WriteUInt16((ushort)Entries.Count);
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
    /// Host -> guest: bolt tightness values for every Screw FSM that is not fully
    /// loose. Guests write the variables and replay Set pos so meshes match the host.
    /// Chunked by the sender.
    /// </summary>
    public sealed class WorldBoltSnapshot : IMessage
    {
        public struct Entry
        {
            public uint NetId;
            public ushort BoltTightness;
            public ushort ScrewInt;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldBoltSnapshot;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16((ushort)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteUInt16(entry.BoltTightness);
                writer.WriteUInt16(entry.ScrewInt);
            }
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
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
        }
    }

    /// <summary>
    /// Host -> guest: Installed/Tightness/Wear for every car part that is not in its
    /// default uninstalled state. Guests write the variables on the part Data FSM.
    /// Chunked by the sender.
    /// </summary>
    public sealed class WorldPartSnapshot : IMessage
    {
        public struct Entry
        {
            public uint NetId;
            public byte Flags;
            public byte Tightness;
            public byte Wear;
        }

        public List<Entry> Entries = new List<Entry>();

        public MessageId Id => MessageId.WorldPartSnapshot;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16((ushort)Entries.Count);
            foreach (var entry in Entries)
            {
                writer.WriteUInt32(entry.NetId);
                writer.WriteByte(entry.Flags);
                writer.WriteByte(entry.Tightness);
                writer.WriteByte(entry.Wear);
            }
        }

        public void Read(NetReader reader)
        {
            int count = reader.ReadUInt16();
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
        }
    }
}
