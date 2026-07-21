namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Guest -> host: periodic report of local need stats (PLAN.md §4.4).
    /// Host stores these in the guest profile sidecar for reconnect.
    /// Wire v28 appends BodyTemp (PLAYER/BodyTemp.Temperature) as the 5th need.
    /// Wire v30 appends Stress (global) and Drunk ("Drunk Mode" FSM DrunkCurrent).
    /// Wire v55 appends Dirtiness (global PlayerDirtiness) after Sequence.
    /// Wire v56 appends HasDirtiness after Dirtiness: false when the guest's
    /// PlayerDirtiness global has not resolved locally, so the host records the
    /// value as unknown and never restores a dirty guest to clean. This decouples
    /// needs reporting from the dirtiness binding — the other seven needs report
    /// even if PlayerDirtiness is (temporarily) unresolvable.
    /// </summary>
    public sealed class PlayerNeedsReport : IMessage
    {
        public byte PlayerId;
        public float Hunger;
        public float Fatigue;
        public float Thirst;
        public float Urine;
        public float BodyTemp;
        public float Stress;
        public float Drunk;
        public ushort Sequence;
        public float Dirtiness;
        public bool HasDirtiness;
        /// <summary>Wire v78: persistent PlayerAlco BAC (drives DUI checkpoints + sobering).</summary>
        public float PlayerAlco;
        /// <summary>Wire v78: false when PlayerAlco has not resolved locally (host records unknown).</summary>
        public bool HasAlco;

        public MessageId Id => MessageId.PlayerNeedsReport;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(PlayerId);
            writer.WriteSingle(Hunger);
            writer.WriteSingle(Fatigue);
            writer.WriteSingle(Thirst);
            writer.WriteSingle(Urine);
            writer.WriteSingle(BodyTemp);
            writer.WriteSingle(Stress);
            writer.WriteSingle(Drunk);
            writer.WriteUInt16(Sequence);
            writer.WriteSingle(Dirtiness);
            writer.WriteByte(HasDirtiness ? (byte)1 : (byte)0);
            writer.WriteSingle(PlayerAlco);
            writer.WriteByte(HasAlco ? (byte)1 : (byte)0);
        }

        public void Read(NetReader reader)
        {
            PlayerId = reader.ReadByte();
            Hunger = reader.ReadSingle();
            Fatigue = reader.ReadSingle();
            Thirst = reader.ReadSingle();
            Urine = reader.ReadSingle();
            BodyTemp = reader.ReadSingle();
            Stress = reader.ReadSingle();
            Drunk = reader.ReadSingle();
            Sequence = reader.ReadUInt16();
            Dirtiness = reader.ReadSingle();
            HasDirtiness = reader.ReadByte() != 0;
            PlayerAlco = reader.ReadSingle();
            HasAlco = reader.ReadByte() != 0;
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

    /// <summary>Host -> all guests: sleep consent round finished (accepted or cancelled).</summary>
    public sealed class SleepConsentResult : IMessage
    {
        public byte RequestId;
        public bool Accepted;

        public MessageId Id => MessageId.SleepConsentResult;

        public void Write(NetWriter writer)
        {
            writer.WriteByte(RequestId);
            writer.WriteByte(Accepted ? (byte)1 : (byte)0);
        }

        public void Read(NetReader reader)
        {
            RequestId = reader.ReadByte();
            Accepted = reader.ReadByte() != 0;
        }
    }
}
