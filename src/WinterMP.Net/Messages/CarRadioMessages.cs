namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: in-car radio tuning/volume (COVERAGE-ROADMAP 8.4). The home
    /// stereo is host-synced; the in-car radio (CORRIS/SORBET) is not. Track content is
    /// cosmetic; tuning/volume is minor shared state. The <b>host</b> owns each car
    /// radio and broadcasts it on change + join; guests apply. See CarRadioSync.
    /// </summary>
    public sealed class CarRadioState : IMessage
    {
        public const byte RadioCorris = 0;
        public const byte RadioSorbet = 1;

        public ushort Sequence;
        public byte RadioId;

        /// <summary>
        /// The tuner's raw <c>Tune</c> float, sent unquantized. This replaced a byte
        /// "Channel" in v81: there is no float named Channel on any radio Knob FSM (the
        /// only Channel is a *string* on the CD player), so the old field bound null and
        /// the station never synced. The station is picked by comparing Tune against
        /// per-station windows inside the game's own FSM, and those bounds are not
        /// knowable from the catalog dump — so carry the float verbatim rather than
        /// quantize it against a guessed range.
        /// </summary>
        public float Tune;

        /// <summary>Knob volume, scaled x100 into a byte by CarRadioSync.</summary>
        public byte Volume;

        public MessageId Id => MessageId.CarRadioState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteByte(RadioId);
            writer.WriteSingle(Tune);
            writer.WriteByte(Volume);
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            RadioId = reader.ReadByte();
            Tune = reader.ReadSingle();
            Volume = reader.ReadByte();
        }
    }
}
