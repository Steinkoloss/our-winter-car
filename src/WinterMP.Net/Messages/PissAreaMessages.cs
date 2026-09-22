namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: yard piss-stain scales (COVERAGE-ROADMAP 8.3). <c>YARD/PissAreas</c>
    /// scales five persistent, world-visible indoor stains on the PISS action; they are
    /// host-saved and shared-visible but run per-client, so the yards disagree. The <b>host</b>
    /// owns the stains and broadcasts the five scales on change + join; guests apply them.
    /// See PissAreaSync.
    /// </summary>
    public sealed class PissAreaState : IMessage
    {
        public ushort Sequence;
        /// <summary>Stain scales, quantized *20 into a byte (0..~12.75 range clamped).</summary>
        public byte Scale1;
        public byte Scale2;
        public byte Scale3;
        public byte Scale4;
        public byte Scale5;

        // v263 append-only: epoch pins the world; revision also supplies a short-lived
        // host freshness challenge. Admissions are renewed for each transport player.
        public uint Epoch, Revision;
        public PissAreaAdmission[] Admissions = new PissAreaAdmission[0];

        public MessageId Id => MessageId.PissAreaState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt16(Sequence);
            writer.WriteByte(Scale1);
            writer.WriteByte(Scale2);
            writer.WriteByte(Scale3);
            writer.WriteByte(Scale4);
            writer.WriteByte(Scale5);
            writer.WriteUInt32(Epoch);
            writer.WriteUInt32(Revision);
            if (Admissions == null || Admissions.Length > 254) throw new ProtocolException("Invalid stain admissions.");
            writer.WriteByte((byte)Admissions.Length);
            foreach (var entry in Admissions) { writer.WriteByte(entry.Actor); writer.WriteUInt64(entry.Token); writer.WriteUInt32(entry.HighWater); }
        }

        public void Read(NetReader reader)
        {
            Sequence = reader.ReadUInt16();
            Scale1 = reader.ReadByte();
            Scale2 = reader.ReadByte();
            Scale3 = reader.ReadByte();
            Scale4 = reader.ReadByte();
            Scale5 = reader.ReadByte();
            Epoch = reader.ReadUInt32();
            Revision = reader.ReadUInt32();
            int count = reader.ReadByte();
            if (count > 254) throw new ProtocolException("Invalid stain admissions.");
            Admissions = new PissAreaAdmission[count];
            for (int i = 0; i < count; i++) Admissions[i] = new PissAreaAdmission {
                Actor = reader.ReadByte(), Token = reader.ReadUInt64(), HighWater = reader.ReadUInt32() };
        }
    }

    public struct PissAreaAdmission
    {
        public byte Actor;
        public ulong Token;
        public uint HighWater;
    }

    public sealed class PissAreaIntent : IMessage
    {
        public uint Epoch, Revision, Sequence;
        public ulong Admission;
        public byte Actor, Area, Action;
        public float Contribution;
        public MessageId Id => MessageId.PissAreaIntent;
        public void Write(NetWriter w) {
            w.WriteUInt32(Epoch); w.WriteUInt32(Revision); w.WriteUInt32(Sequence); w.WriteUInt64(Admission);
            w.WriteByte(Actor); w.WriteByte(Area); w.WriteByte(Action); w.WriteSingle(Contribution);
        }
        public void Read(NetReader r) {
            Epoch=r.ReadUInt32(); Revision=r.ReadUInt32(); Sequence=r.ReadUInt32(); Admission=r.ReadUInt64();
            Actor=r.ReadByte(); Area=r.ReadByte(); Action=r.ReadByte(); Contribution=r.ReadSingle();
        }
    }
}
