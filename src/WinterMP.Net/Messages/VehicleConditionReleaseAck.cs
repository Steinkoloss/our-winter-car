namespace WinterMP.Net.Messages
{
    /// <summary>Host confirmation of the condition paired with one accepted guest vehicle release.</summary>
    public sealed class VehicleConditionReleaseAck : IMessage
    {
        public ushort ReleaseSequence;
        public VehicleCondition Condition = new VehicleCondition();
        public MessageId Id => MessageId.VehicleConditionReleaseAck;
        public bool Valid => VehicleConditionStreamPolicy.IsValid(Condition) && Condition.OwnerPlayerId != 0
            && Condition.Sequence != VehicleCondition.SnapshotSequence;

        public void Write(NetWriter writer)
        {
            if (!Valid) throw new ProtocolException("Invalid vehicle condition release confirmation.");
            writer.WriteUInt16(ReleaseSequence); Condition.Write(writer);
        }
        public void Read(NetReader reader)
        {
            ReleaseSequence = reader.ReadUInt16(); Condition = new VehicleCondition(); Condition.Read(reader);
            if (!Valid) throw new ProtocolException("Invalid vehicle condition release confirmation.");
        }
        public VehicleConditionReleaseAck Copy() => new VehicleConditionReleaseAck {
            ReleaseSequence = ReleaseSequence, Condition = VehicleConditionStreamPolicy.Copy(Condition) };
    }
}
