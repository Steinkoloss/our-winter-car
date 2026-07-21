namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: in-car radio power/channel/volume (COVERAGE-ROADMAP 8.4). The home
    /// stereo is host-synced; the in-car radio (CORRIS/SORBET) is not. Track content is
    /// cosmetic; power/channel/volume is minor shared state. The <b>host</b> owns each car
    /// radio and broadcasts it on change + join; guests apply. See CarRadioSync.
    /// </summary>
    public sealed class CarRadioState : IMessage
    {
        public const byte RadioCorris = 0;
        public const byte RadioSorbet = 1;

        public ushort Sequence;
        public byte RadioId;
        public byte Channel;
        public byte Volume;

        public MessageId Id => MessageId.CarRadioState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteByte(RadioId);
            writer.WriteByte(Channel);
            writer.WriteByte(Volume);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            RadioId = reader.ReadByte();
            Channel = reader.ReadByte();
            Volume = reader.ReadByte();
        }
    }
}
