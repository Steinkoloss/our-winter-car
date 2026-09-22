namespace WinterMP.Net.Messages
{
    /// <summary>Host absolute setting for one cylinder-head valve adjuster.</summary>
    public sealed class ValveAdjustmentState : IMessage
    {
        public uint NetId;
        public float Setting;
        public MessageId Id => MessageId.ValveAdjustmentState;
        public void Write(NetWriter w)
        {
            if (!Sync.ValveAdjustmentPolicy.Valid(this)) throw new ProtocolException("Invalid valve adjustment.");
            w.WriteUInt32(NetId); w.WriteSingle(Setting);
        }
        public void Read(NetReader r)
        {
            NetId = r.ReadUInt32(); Setting = r.ReadSingle();
            if (!Sync.ValveAdjustmentPolicy.Valid(this)) throw new ProtocolException("Invalid valve adjustment.");
        }
    }
}
