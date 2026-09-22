namespace WinterMP.Net.Messages
{
    public enum TaxiCallPhase : byte { Silent, Ringing, Speaking, Finished }
    public enum TaxiCallAction : byte { Answer, HangUp }

    /// <summary>One ordered, absolute view of the native host's taxi service and customer.</summary>
    public sealed class TaxiServiceState : IMessage
    {
        public const byte Nobody = 255;
        public const uint Car = 1, Customer = 2, Phone = 4, Ignition = 8, Character = 16,
            Indicator = 32, PassengerMass = 64, Boarded = 128;
        public uint Revision, CallId, Flags;
        public byte CallOwner = Nobody, ColliderFlags;
        public TaxiCallPhase CallPhase;
        public NetVector3 CustomerPosition, WalkerPosition;
        public NetQuaternion CustomerRotation = NetQuaternion.Identity, WalkerRotation = NetQuaternion.Identity;
        public string Pickup = "", Destination = "", IndicatorText = "", Subtitle = "", Voice = "", RootClip = "", SkeletonClip = "";
        public float RootTime, SkeletonTime;
        public uint LuggageEpoch = 1;
        public byte LuggageMask;
        public NetVector3[] LuggagePositions = new NetVector3[5];
        public NetQuaternion[] LuggageRotations = { NetQuaternion.Identity, NetQuaternion.Identity, NetQuaternion.Identity,
            NetQuaternion.Identity, NetQuaternion.Identity };
        public uint PaydayId = 1;
        public byte PaydayFlags;
        public const byte UnreadPayday = 1, PaydayEnvelope = 2;
        public float[] PaydayRundown = new float[8];
        public MessageId Id => MessageId.TaxiServiceState;
        public void Write(NetWriter w)
        {
            Validate();
            w.WriteUInt32(Revision); w.WriteUInt32(CallId); w.WriteUInt32(Flags);
            w.WriteByte(CallOwner); w.WriteByte(ColliderFlags); w.WriteByte((byte)CallPhase);
            w.WriteVector3(CustomerPosition); w.WriteQuaternion(CustomerRotation);
            w.WriteVector3(WalkerPosition); w.WriteQuaternion(WalkerRotation);
            w.WriteString(Pickup); w.WriteString(Destination); w.WriteString(IndicatorText);
            w.WriteString(Subtitle); w.WriteString(Voice); w.WriteString(RootClip); w.WriteString(SkeletonClip);
            w.WriteSingle(RootTime); w.WriteSingle(SkeletonTime);
            w.WriteUInt32(LuggageEpoch); w.WriteByte(LuggageMask);
            for (int i = 0; i < 5; i++) { w.WriteVector3(LuggagePositions[i]); w.WriteQuaternion(LuggageRotations[i]); }
            w.WriteUInt32(PaydayId); w.WriteByte(PaydayFlags);
            for (int i = 0; i < 8; i++) w.WriteSingle(PaydayRundown[i]);
        }
        public void Read(NetReader r)
        {
            Revision = r.ReadUInt32(); CallId = r.ReadUInt32(); Flags = r.ReadUInt32();
            CallOwner = r.ReadByte(); ColliderFlags = r.ReadByte(); CallPhase = (TaxiCallPhase)r.ReadByte();
            CustomerPosition = r.ReadVector3(); CustomerRotation = r.ReadQuaternion();
            WalkerPosition = r.ReadVector3(); WalkerRotation = r.ReadQuaternion();
            Pickup = r.ReadString(); Destination = r.ReadString(); IndicatorText = r.ReadString();
            Subtitle = r.ReadString(); Voice = r.ReadString(); RootClip = r.ReadString(); SkeletonClip = r.ReadString();
            RootTime = r.ReadSingle(); SkeletonTime = r.ReadSingle();
            LuggageEpoch = r.ReadUInt32(); LuggageMask = r.ReadByte();
            for (int i = 0; i < 5; i++) { LuggagePositions[i] = r.ReadVector3(); LuggageRotations[i] = r.ReadQuaternion(); }
            PaydayId = r.ReadUInt32(); PaydayFlags = r.ReadByte();
            for (int i = 0; i < 8; i++) PaydayRundown[i] = r.ReadSingle();
            Validate();
        }
        private void Validate()
        {
            if (!Sync.TaxiServicePolicy.Valid(this)) throw new ProtocolException("Invalid taxi service state.");
        }
    }

    /// <summary>Closing the local salary sheet acknowledges only the report that was opened.</summary>
    public sealed class TaxiPaydayReadIntent : IMessage
    {
        public byte PlayerId;
        public uint PaydayId;
        public MessageId Id => MessageId.TaxiPaydayReadIntent;
        public void Write(NetWriter w) { Validate(); w.WriteByte(PlayerId); w.WriteUInt32(PaydayId); }
        public void Read(NetReader r) { PlayerId = r.ReadByte(); PaydayId = r.ReadUInt32(); Validate(); }
        private void Validate()
        {
            if (PlayerId == TaxiServiceState.Nobody || PaydayId == 0) throw new ProtocolException("Invalid taxi payday acknowledgment.");
        }
    }

    /// <summary>The transport authenticates PlayerId; the host checks this particular incoming call.</summary>
    public sealed class TaxiCallIntent : IMessage
    {
        public byte PlayerId;
        public uint CallId;
        public TaxiCallAction Action;
        public MessageId Id => MessageId.TaxiCallIntent;
        public void Write(NetWriter w)
        {
            Validate(); w.WriteByte(PlayerId); w.WriteUInt32(CallId); w.WriteByte((byte)Action);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); CallId = r.ReadUInt32(); Action = (TaxiCallAction)r.ReadByte(); Validate();
        }
        private void Validate()
        {
            if (PlayerId == TaxiServiceState.Nobody || CallId == 0 || (byte)Action > 1)
                throw new ProtocolException("Invalid taxi call intent.");
        }
    }
}
