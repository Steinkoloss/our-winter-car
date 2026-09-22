using System;
using WinterMP.Net.Sync;

namespace WinterMP.Net.Messages
{
    public sealed class WoodstoveFeedIntent : IMessage
    {
        public uint SourceId, Epoch, Sequence, ResourceId;
        public byte Actor;
        public MessageId Id => MessageId.WoodstoveFeedIntent;
        public WoodstoveFeedRequest Request() => new WoodstoveFeedRequest(SourceId, Epoch, Actor, Sequence, ResourceId);
        public void Write(NetWriter w)
        { w.WriteUInt32(SourceId); w.WriteUInt32(Epoch); w.WriteByte(Actor); w.WriteUInt32(Sequence); w.WriteUInt32(ResourceId); }
        public void Read(NetReader r)
        { SourceId = r.ReadUInt32(); Epoch = r.ReadUInt32(); Actor = r.ReadByte(); Sequence = r.ReadUInt32(); ResourceId = r.ReadUInt32(); }
    }

    // Admission, result and periodic observation share one ordered absolute stream.
    // A resource descriptor uses a host-assigned ID, never a path or Unity instance ID.
    public sealed class WoodstoveFuelUpdate : IMessage
    {
        public const int MaxResources = 256;
        private uint _epoch;
        public uint Epoch { get => Snapshot != null ? Snapshot.Epoch : _epoch; set => _epoch = value; }
        public WoodstoveFuelSnapshot? Snapshot;
        public byte Actor = 255, Shape;
        public uint Sequence, HighWater, ResourceId;
        public bool IsDecision;
        public WoodstoveFeedStatus Status;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public MessageId Id => MessageId.WoodstoveFuelUpdate;
        private void Validate()
        {
            if (Epoch == 0 || (uint)Status > (uint)WoodstoveFeedStatus.Pending || Shape > 2
                || (ResourceId != 0 && (Shape == 0 || !Finite(Position.X) || !Finite(Position.Y) || !Finite(Position.Z)
                    || !Finite(Rotation.X) || !Finite(Rotation.Y) || !Finite(Rotation.Z) || !Finite(Rotation.W)))
                || (IsDecision && Status == WoodstoveFeedStatus.Accepted && Snapshot == null))
                throw new ProtocolException("Invalid cabin fuel update.");
            if (Snapshot == null) return;
            var ids = Snapshot.ConsumedResources;
            if (Snapshot.SourceId != WoodstoveFuelAuthority.CabinSourceId || Snapshot.Revision == 0
                || !Snapshot.Values.Valid || Snapshot.Values.Fuel > 4 || ids.Length > MaxResources)
                throw new ProtocolException("Invalid cabin fuel snapshot.");
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == 0 || (i > 0 && ids[i] <= ids[i - 1])) throw new ProtocolException("Invalid cabin retirement set.");
        }
        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        public void Write(NetWriter w)
        {
            Validate(); w.WriteUInt32(Epoch); w.WriteByte(Actor); w.WriteUInt32(Sequence); w.WriteUInt32(HighWater);
            w.WriteBool(IsDecision); w.WriteByte((byte)Status); w.WriteBool(Snapshot != null);
            if (Snapshot != null)
            {
                w.WriteUInt32(Snapshot.SourceId); w.WriteUInt32(Snapshot.Revision); w.WriteInt32(Snapshot.Values.Fuel);
                w.WriteSingle(Snapshot.Values.Heat); w.WriteBool(Snapshot.Values.Lit);
                var ids = Snapshot.ConsumedResources; w.WriteUInt16((ushort)ids.Length);
                foreach (uint id in ids) w.WriteUInt32(id);
            }
            w.WriteUInt32(ResourceId); w.WriteByte(Shape); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            Snapshot = null; Epoch = r.ReadUInt32(); Actor = r.ReadByte(); Sequence = r.ReadUInt32(); HighWater = r.ReadUInt32();
            IsDecision = r.ReadBool(); Status = (WoodstoveFeedStatus)r.ReadByte();
            if (r.ReadBool())
            {
                uint source = r.ReadUInt32(), revision = r.ReadUInt32();
                var values = new WoodstoveFuelValues(r.ReadInt32(), r.ReadSingle(), r.ReadBool());
                int count = r.ReadUInt16(); if (count > MaxResources) throw new ProtocolException("Cabin retirement set too large.");
                var ids = new uint[count]; for (int i = 0; i < count; i++) ids[i] = r.ReadUInt32();
                Snapshot = new WoodstoveFuelSnapshot(source, Epoch, revision, values, ids);
            }
            ResourceId = r.ReadUInt32(); Shape = r.ReadByte(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion(); Validate();
        }
    }
}
