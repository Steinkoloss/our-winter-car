using WinterMP.Net.Sync;

namespace WinterMP.Net.Messages
{
    public enum ScraperOperation : byte { Pickup, Equip, Off, Drop, KeepAlive, Stroke }

    // Eye/direction are untrusted input, anchored to the host's latest accepted
    // actor transform before host physics is queried. Never includes a cutoff.
    public sealed class ScraperAction : IMessage
    {
        public uint Epoch, Sequence, VehicleId, ToolId;
        public byte Actor, Pane;
        public ScraperOperation Operation;
        public NetVector3 Eye, Direction;
        public MessageId Id => MessageId.ScraperAction;
        public void Write(NetWriter w)
        {
            w.WriteUInt32(Epoch); w.WriteByte(Actor); w.WriteUInt32(Sequence);
            w.WriteUInt32(VehicleId); w.WriteByte(Pane); w.WriteUInt32(ToolId);
            w.WriteByte((byte)Operation); w.WriteVector3(Eye); w.WriteVector3(Direction);
        }
        public void Read(NetReader r)
        {
            Epoch = r.ReadUInt32(); Actor = r.ReadByte(); Sequence = r.ReadUInt32();
            VehicleId = r.ReadUInt32(); Pane = r.ReadByte(); ToolId = r.ReadUInt32();
            Operation = (ScraperOperation)r.ReadByte(); Eye = r.ReadVector3(); Direction = r.ReadVector3();
        }
        public PaneScrapeIntent Intent() => new PaneScrapeIntent(Epoch, Actor, Sequence, VehicleId, Pane, ToolId);
    }

    public sealed class PaneScrapeUpdate : IMessage
    {
        public uint Epoch, VehicleId, Revision, ToolId, Sequence, HighWater;
        public byte Actor = 255, Holder = 255;
        public bool Equipped, IsDecision;
        public float Cutoff;
        public PaneScrapeStatus Status;
        public MessageId Id => MessageId.PaneScrapeUpdate;
        public PaneScrapeSnapshot Snapshot() => new PaneScrapeSnapshot(VehicleId, Epoch, Revision, Cutoff);
        public PaneScrapeDecision Decision() => new PaneScrapeDecision(Status, Actor, Sequence, Snapshot());
        private void Validate()
        {
            if (!PaneScrapePolicy.Finite(Cutoff) || (uint)Status > (uint)PaneScrapeStatus.NativeFailure)
                throw new ProtocolException("Invalid pane scrape update.");
        }
        public void Write(NetWriter w)
        {
            Validate();
            w.WriteUInt32(Epoch); w.WriteUInt32(VehicleId); w.WriteUInt32(Revision); w.WriteSingle(Cutoff);
            w.WriteUInt32(ToolId); w.WriteByte(Holder); w.WriteBool(Equipped);
            w.WriteByte(Actor); w.WriteUInt32(Sequence); w.WriteUInt32(HighWater);
            w.WriteBool(IsDecision); w.WriteByte((byte)Status);
        }
        public void Read(NetReader r)
        {
            Epoch = r.ReadUInt32(); VehicleId = r.ReadUInt32(); Revision = r.ReadUInt32(); Cutoff = r.ReadSingle();
            ToolId = r.ReadUInt32(); Holder = r.ReadByte(); Equipped = r.ReadBool();
            Actor = r.ReadByte(); Sequence = r.ReadUInt32(); HighWater = r.ReadUInt32();
            IsDecision = r.ReadBool(); Status = (PaneScrapeStatus)r.ReadByte(); Validate();
        }
    }
}
