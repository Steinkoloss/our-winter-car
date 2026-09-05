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
        public float BankBalance;
        public float NetIncome;
        public byte Flags;

        public const byte FlagBankBalance = 1;
        public const byte FlagNetIncome = 2;

        public MessageId Id => MessageId.WalletState;

        public void Write(NetWriter writer)
        {
            writer.WriteSingle(Money);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(BankBalance);
            writer.WriteSingle(NetIncome);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Money = reader.ReadSingle();
            Sequence = reader.ReadUInt16();
            BankBalance = reader.ReadSingle();
            NetIncome = reader.ReadSingle();
            Flags = reader.ReadByte();
        }
    }

    /// <summary>Guest ATM request. Positive amounts deposit cash; negative amounts withdraw it.</summary>
    public sealed class BankTransferIntent : IMessage
    {
        public byte PlayerId;
        public ushort Sequence;
        public short Amount;
        public MessageId Id => MessageId.BankTransferIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteUInt16(unchecked((ushort)Amount));
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Amount = unchecked((short)reader.ReadUInt16());
        }
    }

    /// <summary>Host acknowledges one transfer, including a terminal rejection.</summary>
    public sealed class BankTransferResult : IMessage
    {
        public byte PlayerId;
        public ushort Sequence;
        public bool Accepted;
        public MessageId Id => MessageId.BankTransferResult;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteBool(Accepted);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Accepted = reader.ReadBool();
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
