namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: shared flea-market sale-table state (COVERAGE-ROADMAP 3.1). The
    /// day-timed random sale runs per-client, so items sell on different days and the
    /// proceeds diverge. The <b>host</b> owns the table: it runs the sale RNG and broadcasts
    /// the accumulated proceeds + rent so both peers agree; guests suppress their local sale
    /// RNG and apply this. Rent + payout ride the shared wallet (proceeds credit the host
    /// wallet via the sale, rent via a relayed intent). Per-item placement/pricing stays
    /// local (dynamic picked-object refs — deliberately open, see PLAN §4.4). See FleaSaleSync.
    /// </summary>
    public sealed class FleaSaleState : IMessage
    {
        /// <summary>The table is rented / active.</summary>
        public const byte FlagRented = 1;

        public ushort Sequence;
        public float MoneyTotal;
        public ushort RentDays;
        public byte Flags;

        public MessageId Id => MessageId.FleaSaleState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(MoneyTotal);
            writer.WriteUInt16(RentDays);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            MoneyTotal = reader.ReadSingle();
            RentDays = reader.ReadUInt16();
            Flags = reader.ReadByte();
        }
    }

    /// <summary>
    /// Guest -> host: a flea sale-table action. The host validates fresh nearby pose +
    /// monotonic sequence and fires the real game event on its authoritative table so the
    /// shared wallet is debited/credited once. Only the rent press uses this — envelope
    /// collection rides the catalogued MoneyFlea control via FsmStateEnter.
    /// </summary>
    public sealed class FleaSaleIntent : IMessage
    {
        public const byte ActionRent = 0;
        /// <summary>Reserved-unused (never reuse): collection needs no intent; hosts reject it.</summary>
        public const byte ActionCollect = 1;

        public byte Action;
        public byte PlayerId;
        public ushort Sequence;

        public MessageId Id => MessageId.FleaSaleIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Action);
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            Action = reader.ReadByte();
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }
}
