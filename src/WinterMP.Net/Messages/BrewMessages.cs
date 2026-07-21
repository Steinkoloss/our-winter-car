namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Owner -> host -> peers: the fermenting state of a kilju bucket (COVERAGE-ROADMAP 3.3).
    /// The bucket's <c>Alcohol</c>/<c>BrewTime</c> advance on the (host) clock and its
    /// ingredients are added locally, so without this each client brews a different quality
    /// and the buyer pays a different price. Carried like <see cref="FluidContainerState"/>:
    /// keyed to the tracked bucket item id, streamed by whoever holds it, applied by others.
    /// Selling routes payout through the host wallet (the KiljuBuyer pay is catalogued), and
    /// the price derives from this agreed brew. See KiljuSync.
    /// </summary>
    public sealed class BrewState : IMessage
    {
        public const byte FlagFinished = 1;
        public const byte FlagLidOn = 2;

        public uint ItemId;
        public byte OwnerPlayerId;
        public ushort Sequence;
        public byte Flags;
        public float Alcohol;
        public float BrewTime;

        public bool Finished => (Flags & FlagFinished) != 0;
        public bool LidOn => (Flags & FlagLidOn) != 0;

        public MessageId Id => MessageId.BrewState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(ItemId);
            writer.WriteByte(OwnerPlayerId);
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Flags);
            writer.WriteSingle(Alcohol);
            writer.WriteSingle(BrewTime);
        }

        public void Read(NetReader reader)
        {
            ItemId = reader.ReadUInt32();
            OwnerPlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
            Flags = reader.ReadByte();
            Alcohol = reader.ReadSingle();
            BrewTime = reader.ReadSingle();
        }
    }
}
