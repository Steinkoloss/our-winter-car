namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> all: authoritative state of a utility meter (electricity / phone bill).
    /// The bill accrual, cutoff and blackout run per-client off the shared host clock, so
    /// without this each home disagrees about how much is owed and whether the power is on.
    /// The host owns the ledger and broadcasts the unpaid total + power/line state on change
    /// + join; guests write it back so both homes black out together. Bill payment routes
    /// through the normal host purchase path (the Pay buttons are catalogued buys), not this
    /// message. See UtilityBillSync.
    /// </summary>
    public sealed class UtilityBillState : IMessage
    {
        public const byte MeterElectricity1 = 0;
        public const byte MeterElectricity2 = 1;
        public const byte MeterPhone1 = 2;
        public const byte MeterPhone2 = 3;

        /// <summary>Electricity: <c>MainSwitch</c> on (not cut off). Phone: line still connected (<c>PhonePaid</c>).</summary>
        public const byte FlagPowerOn = 1;

        public byte Meter;
        public float UnpaidBills;
        public byte Flags;

        public bool PowerOn => (Flags & FlagPowerOn) != 0;

        public MessageId Id => MessageId.UtilityBillState;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(Meter);
            writer.WriteSingle(UnpaidBills);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Meter = reader.ReadByte();
            UnpaidBills = reader.ReadSingle();
            Flags = reader.ReadByte();
        }
    }
}
