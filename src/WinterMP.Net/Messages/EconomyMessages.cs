namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host-authoritative shared wallet balance. Guests overwrite their local
    /// money global and HUD display on every message (host wins).
    /// </summary>
    public sealed class WalletState : IMessage
    {
        public float Money;
        public ushort Sequence;

        public MessageId Id => MessageId.WalletState;

        public void Write(NetWriter writer)
        {
            writer.WriteSingle(Money);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            Money = reader.ReadSingle();
            Sequence = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// Guest -> host: "run this buy event on your machine". Guests abort their
    /// local purchase guard before money changes; the host fires the event and
    /// broadcasts the resulting FSM state + wallet.
    /// </summary>
    public sealed class PurchaseIntent : IMessage
    {
        public byte PlayerId;
        public uint NetId;
        public string EventName = string.Empty;
        public ushort Sequence;

        public MessageId Id => MessageId.PurchaseIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt32(NetId);
            writer.WriteString(EventName);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            NetId = reader.ReadUInt32();
            EventName = reader.ReadString();
            Sequence = reader.ReadUInt16();
        }
    }
}
