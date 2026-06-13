namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Guest -> host: periodic report of local need stats (PLAN.md §4.4).
    /// Host stores these in the guest profile sidecar for reconnect.
    /// </summary>
    public sealed class PlayerNeedsReport : IMessage
    {
        public byte PlayerId;
        public float Hunger;
        public float Fatigue;
        public float Thirst;
        public float Urine;
        public ushort Sequence;

        public MessageId Id => MessageId.PlayerNeedsReport;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteSingle(Hunger);
            writer.WriteSingle(Fatigue);
            writer.WriteSingle(Thirst);
            writer.WriteSingle(Urine);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Hunger = reader.ReadSingle();
            Fatigue = reader.ReadSingle();
            Thirst = reader.ReadSingle();
            Urine = reader.ReadSingle();
            Sequence = reader.ReadUInt16();
        }
    }

    /// <summary>Host -> all guests: host initiated sleep / time skip — guests must accept.</summary>
    public sealed class SleepConsentRequest : IMessage
    {
        public byte RequestId;
        public byte InitiatorPlayerId;

        public MessageId Id => MessageId.SleepConsentRequest;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(RequestId);
            writer.WriteByte(InitiatorPlayerId);
        }

        public void Read(NetReader reader)
        {
            RequestId = reader.ReadByte();
            InitiatorPlayerId = reader.ReadByte();
        }
    }

    /// <summary>Guest -> host: accept or decline a sleep consent round.</summary>
    public sealed class SleepConsentResponse : IMessage
    {
        public byte RequestId;
        public byte PlayerId;
        public bool Accepted;

        public MessageId Id => MessageId.SleepConsentResponse;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(RequestId);
            writer.WriteByte(PlayerId);
            writer.WriteByte(Accepted ? (byte)1 : (byte)0);
        }

        public void Read(NetReader reader)
        {
            RequestId = reader.ReadByte();
            PlayerId = reader.ReadByte();
            Accepted = reader.ReadByte() != 0;
        }
    }
}
