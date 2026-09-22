using WinterMP.Net.Sync;
namespace WinterMP.Net.Messages
{
    public sealed class SaunaTimerIntent : IMessage
    {
        public uint SourceId, Epoch, Sequence, ExpectedRevision;
        public byte Actor;
        public float Timer;
        public NetVector3 Eye, Direction;
        public MessageId Id => MessageId.SaunaTimerIntent;
        public void Write(NetWriter w)
        { w.WriteUInt32(SourceId); w.WriteUInt32(Epoch); w.WriteByte(Actor); w.WriteUInt32(Sequence); w.WriteUInt32(ExpectedRevision); w.WriteSingle(Timer); w.WriteVector3(Eye); w.WriteVector3(Direction); }
        public void Read(NetReader r)
        { SourceId=r.ReadUInt32(); Epoch=r.ReadUInt32(); Actor=r.ReadByte(); Sequence=r.ReadUInt32(); ExpectedRevision=r.ReadUInt32(); Timer=r.ReadSingle(); Eye=r.ReadVector3(); Direction=r.ReadVector3(); }
    }
    public sealed class SaunaTimerState : IMessage
    {
        public const byte Observation=0, Accepted=1, Rejected=2;
        public uint SourceId, Epoch, Revision, Sequence, HighWater;
        public byte Actor=255, Status;
        public float Timer, Time, KnobAngle;
        public MessageId Id => MessageId.SaunaTimerState;
        public bool Valid => SourceId == SaunaTimerAuthority.SourceId && Epoch != 0 && Revision != 0 && Status <= Rejected
            && (Status == Observation || (Actor < 255 && Sequence > 0 && HighWater >= Sequence))
            && new SaunaTimerValues { Timer=Timer, Time=Time, KnobAngle=KnobAngle }.Valid;
        public SaunaTimerState Copy() => (SaunaTimerState)MemberwiseClone();
        public void Write(NetWriter w)
        {
            if (!Valid) throw new ProtocolException("Invalid sauna timer state.");
            w.WriteUInt32(SourceId); w.WriteUInt32(Epoch); w.WriteUInt32(Revision); w.WriteByte(Actor); w.WriteUInt32(Sequence);
            w.WriteUInt32(HighWater); w.WriteByte(Status); w.WriteSingle(Timer); w.WriteSingle(Time); w.WriteSingle(KnobAngle);
        }
        public void Read(NetReader r)
        {
            SourceId=r.ReadUInt32(); Epoch=r.ReadUInt32(); Revision=r.ReadUInt32(); Actor=r.ReadByte(); Sequence=r.ReadUInt32();
            HighWater=r.ReadUInt32(); Status=r.ReadByte(); Timer=r.ReadSingle(); Time=r.ReadSingle(); KnobAngle=r.ReadSingle();
            if (!Valid) throw new ProtocolException("Invalid sauna timer state.");
        }
    }
}
