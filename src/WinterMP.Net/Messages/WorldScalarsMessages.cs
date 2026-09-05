namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests (v86): small host-owned world scalars that each re-roll or progress
    /// per-client — daily scrap-metal price (COVERAGE-ROADMAP R1.5), the bank prime interest
    /// rate (the rate half of R1.1; the balance itself is a PlayMaker global gated on a
    /// fresh dump), and the player-key/progression record `Database/Keys :: PlayerKeys`
    /// (R1.7: UncleStage, GIFU key, Conline phone number). Host broadcasts on change +
    /// keepalive + join; guests write the values back so prices, interest and progression
    /// agree. See WorldScalarsSync.
    /// </summary>
    public sealed class WorldScalarsState : IMessage
    {
        /// <summary>The GIFU truck key is held (Keys <c>Gifu</c>).</summary>
        public const byte FlagGifuKey = 1;

        public ushort Sequence;
        public float ScrapPriceMKkg;
        public float ScrapChange;
        public float PrimeInterest;
        public byte UncleStage;
        public int ConlineNumber;
        public byte Flags;

        public bool GifuKey => (Flags & FlagGifuKey) != 0;

        public MessageId Id => MessageId.WorldScalarsState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(ScrapPriceMKkg);
            writer.WriteSingle(ScrapChange);
            writer.WriteSingle(PrimeInterest);
            writer.WriteByte(UncleStage);
            writer.WriteInt32(ConlineNumber);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            ScrapPriceMKkg = reader.ReadSingle();
            ScrapChange = reader.ReadSingle();
            PrimeInterest = reader.ReadSingle();
            UncleStage = reader.ReadByte();
            ConlineNumber = reader.ReadInt32();
            Flags = reader.ReadByte();
        }
    }
}
