namespace WinterMP.Net.Messages
{
    /// <summary>Host table observations. Outcomes describe native result text, never a payment command.</summary>
    public sealed class VenttiTableState : IMessage
    {
        public const byte None = 0, Win = 1, Lose = 2, WinCar = 3, LoseCar = 4, WinHouse = 5, LoseHouse = 6;

        public uint TableId;
        public uint Sequence;
        public float Stake;
        public int PlayerTotal;
        public int HouseTotal;
        public byte Outcome;

        public MessageId Id => MessageId.VenttiTableState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(TableId);
            writer.WriteUInt32(Sequence);
            writer.WriteSingle(Stake);
            writer.WriteInt32(PlayerTotal);
            writer.WriteInt32(HouseTotal);
            writer.WriteByte(Outcome);
        }

        public void Read(NetReader reader)
        {
            TableId = reader.ReadUInt32();
            Sequence = reader.ReadUInt32();
            Stake = reader.ReadSingle();
            PlayerTotal = reader.ReadInt32();
            HouseTotal = reader.ReadInt32();
            Outcome = reader.ReadByte();
        }
    }
}
