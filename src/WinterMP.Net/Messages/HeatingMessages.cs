namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> all: authoritative state of a home heat source (cabin woodstove, sauna
    /// kiuas, cottage/living-room fireplace) — PLAN.md §4.8. Lit/fuel/heat and sauna
    /// temperature are host-owned so everyone "freezes in the same lake, thaws at the
    /// same sauna". Guests apply these back onto their local FSM so each client's own
    /// position-derived body-temp calc warms consistently. See HeatSourceSync.
    /// </summary>
    public sealed class HeatSourceState : IMessage
    {
        /// <summary>The source is currently burning / has live embers.</summary>
        public const byte FlagLit = 1;

        /// <summary>Stable scene-path hash of the source's container (see PLAN §4.1).</summary>
        public uint SourceId;
        public byte Flags;
        /// <summary>Firewood loaded, clamped to a byte (0 when unknown).</summary>
        public byte Fuel;
        /// <summary>Heat output, 0..255 (stove/löyly heat). Drives shared warmth.</summary>
        public byte HeatOutput;
        /// <summary>Sauna air temperature * 100, clamped to a ushort; 0 for non-sauna sources.</summary>
        public ushort SaunaTemp;

        public bool IsLit => (Flags & FlagLit) != 0;

        public MessageId Id => MessageId.HeatSourceState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(SourceId);
            writer.WriteByte(Flags);
            writer.WriteByte(Fuel);
            writer.WriteByte(HeatOutput);
            writer.WriteUInt16(SaunaTemp);
        }

        public void Read(NetReader reader)
        {
            SourceId = reader.ReadUInt32();
            Flags = reader.ReadByte();
            Fuel = reader.ReadByte();
            HeatOutput = reader.ReadByte();
            SaunaTemp = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// Guest -> host: an anyone-triggers action on a shared heat source (PLAN §4.8).
    /// The host validates the source id, fires the corresponding game FSM event on its
    /// authoritative instance, and the resulting <see cref="HeatSourceState"/> broadcast
    /// reflects the progression. Firewood delivery / lighting / grilling / löyly.
    /// </summary>
    public sealed class HeatSourceIntent : IMessage
    {
        public const byte ActionLight = 0;
        public const byte ActionFeedWood = 1;
        public const byte ActionGrill = 2;
        public const byte ActionSaunaThrow = 3;

        public uint SourceId;
        public byte Action;
        /// <summary>Authenticated sender identity; appended in protocol v49.</summary>
        public byte PlayerId;
        /// <summary>Per-player monotonic action sequence; appended in protocol v49.</summary>
        public ushort Sequence;

        public MessageId Id => MessageId.HeatSourceIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(SourceId);
            writer.WriteByte(Action);
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            SourceId = reader.ReadUInt32();
            Action = reader.ReadByte();
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }
}
