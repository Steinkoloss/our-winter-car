namespace WinterMP.Net.Messages
{
    public sealed class DebtLetterState : IMessage
    {
        public uint Revision;
        public float Debt, Total;
        public bool Available;
        public MessageId Id => MessageId.DebtLetterState;
        public void Write(NetWriter w) { w.WriteUInt32(Revision); w.WriteSingle(Debt); w.WriteSingle(Total); w.WriteBool(Available); }
        public void Read(NetReader r) { Revision = r.ReadUInt32(); Debt = r.ReadSingle(); Total = r.ReadSingle(); Available = r.ReadBool(); }
    }

    public sealed class DebtPaymentIntent : IMessage
    {
        public byte PlayerId;
        public ushort Sequence;
        public uint Revision;
        public MessageId Id => MessageId.DebtPaymentIntent;
        public void Write(NetWriter w) { w.WriteByte(PlayerId); w.WriteUInt16(Sequence); w.WriteUInt32(Revision); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); Sequence = r.ReadUInt16(); Revision = r.ReadUInt32(); }
    }

    public sealed class DebtPaymentResult : IMessage
    {
        public const byte Accepted = 0, Changed = 1, Unavailable = 2, Distant = 3, Funds = 4, Stale = 255;
        public byte PlayerId, Result;
        public ushort Sequence;
        public float Paid;
        public MessageId Id => MessageId.DebtPaymentResult;
        public void Write(NetWriter w) { w.WriteByte(PlayerId); w.WriteUInt16(Sequence); w.WriteByte(Result); w.WriteSingle(Paid); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); Sequence = r.ReadUInt16(); Result = r.ReadByte(); Paid = r.ReadSingle(); }
    }

    /// <summary>
    /// Host -> guests: the whole <c>Systems/Expenses</c> record (COVERAGE-ROADMAP 3.6 Kela +
    /// R1.2 rent/housing benefit, v84). The benefit calc, the weekly rent debit and the
    /// KICKOUT eviction all run per-client off the shared clock, so peers disagree on claim
    /// status, rent debt, and — worst — whether the eviction (furniture destruction +
    /// relocation) happened. The <b>host</b> owns all three FSMs and broadcasts on change +
    /// join; guests apply the scalars, replay the eviction once, and keep their own
    /// Rent/Livingsupport FSMs suppressed (a guest plays in the host's world — its local
    /// weekly ticks are throwaway divergence). See WelfareSync.
    /// </summary>
    public sealed class WelfareState : IMessage
    {
        /// <summary>An unemployment claim is active (Kela <c>Continue</c>).</summary>
        public const byte FlagClaiming = 1;
        /// <summary>The rent FSM reached its terminal <c>Kick out</c> state (appended v84).</summary>
        public const byte FlagEvicted = 2;

        public ushort Sequence;
        public int UnemployDays;
        public float PaidAmount;
        public float Weekly;
        public byte Flags;
        /// <summary>Accrued unpaid rent (Rent <c>Debt</c>). Appended v84.</summary>
        public float RentDebt;
        /// <summary>Weekly rent rate (Rent <c>RentPerWeek</c>). Appended v84.</summary>
        public float RentPerWeek;
        /// <summary>Weekly housing benefit (Livingsupport <c>AsumistukiPerWeek</c>). Appended v84.</summary>
        public float AsumistukiPerWeek;

        public bool Claiming => (Flags & FlagClaiming) != 0;
        public bool Evicted => (Flags & FlagEvicted) != 0;

        public MessageId Id => MessageId.WelfareState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteInt32(UnemployDays);
            writer.WriteSingle(PaidAmount);
            writer.WriteSingle(Weekly);
            writer.WriteByte(Flags);
            writer.WriteSingle(RentDebt);
            writer.WriteSingle(RentPerWeek);
            writer.WriteSingle(AsumistukiPerWeek);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            UnemployDays = reader.ReadInt32();
            PaidAmount = reader.ReadSingle();
            Weekly = reader.ReadSingle();
            Flags = reader.ReadByte();
            RentDebt = reader.ReadSingle();
            RentPerWeek = reader.ReadSingle();
            AsumistukiPerWeek = reader.ReadSingle();
        }
    }
}
