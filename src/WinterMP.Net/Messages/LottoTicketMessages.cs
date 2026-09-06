namespace WinterMP.Net.Messages
{
    public sealed class LottoTicketRequest : IMessage
    {
        public const byte Buy = 0, Claim = 1;
        public byte PlayerId, Operation, LineCount;
        public ulong Token;
        public uint Sequence;
        public int Round;
        public string TicketId = string.Empty;
        public byte[] Numbers = new byte[21];
        public MessageId Id => MessageId.LottoTicketRequest;
        public void Write(NetWriter w)
        {
            if (Numbers == null || Numbers.Length != 21) throw new ProtocolException("Invalid Lotto ticket row length.");
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence); w.WriteByte(Operation);
            w.WriteInt32(Round); w.WriteByte(LineCount); w.WriteString(TicketId);
            for (int i = 0; i < 21; i++) w.WriteByte(Numbers[i]);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32(); Operation = r.ReadByte();
            Round = r.ReadInt32(); LineCount = r.ReadByte(); TicketId = r.ReadString();
            Numbers = new byte[21]; for (int i = 0; i < 21; i++) Numbers[i] = r.ReadByte();
        }
    }

    public sealed class LottoTicketReceipt : IMessage
    {
        public const byte Accepted = 0, Invalid = 1, Changed = 2, Distant = 3, Funds = 4,
            Unavailable = 5, Redeemed = 6, Stale = 7;
        public byte PlayerId, Result, Destination;
        public ulong Token;
        public uint Sequence;
        public string TicketId = string.Empty;
        public float Amount;
        public MessageId Id => MessageId.LottoTicketReceipt;
        public void Write(NetWriter w)
        {
            w.WriteByte(PlayerId); w.WriteUInt64(Token); w.WriteUInt32(Sequence); w.WriteByte(Result);
            w.WriteString(TicketId); w.WriteSingle(Amount); w.WriteByte(Destination);
        }
        public void Read(NetReader r)
        {
            PlayerId = r.ReadByte(); Token = r.ReadUInt64(); Sequence = r.ReadUInt32(); Result = r.ReadByte();
            TicketId = r.ReadString(); Amount = r.ReadSingle(); Destination = r.ReadByte();
        }
    }

    /// <summary>Host ticket identity/content; movement uses the existing ItemTransform stream.</summary>
    public sealed class LottoTicketState : IMessage
    {
        public uint Sequence;
        public string TicketId = string.Empty;
        public int Round;
        public byte[] Numbers = new byte[21];
        public float Winnings;
        public bool Retired;
        public NetVector3 Position;
        public NetQuaternion Rotation;
        public MessageId Id => MessageId.LottoTicketState;
        public void Write(NetWriter w)
        {
            if (Numbers == null || Numbers.Length != 21) throw new ProtocolException("Invalid Lotto ticket row length.");
            w.WriteUInt32(Sequence); w.WriteString(TicketId); w.WriteInt32(Round);
            for (int i = 0; i < 21; i++) w.WriteByte(Numbers[i]);
            w.WriteSingle(Winnings); w.WriteBool(Retired); w.WriteVector3(Position); w.WriteQuaternion(Rotation);
        }
        public void Read(NetReader r)
        {
            Sequence = r.ReadUInt32(); TicketId = r.ReadString(); Round = r.ReadInt32();
            Numbers = new byte[21]; for (int i = 0; i < 21; i++) Numbers[i] = r.ReadByte();
            Winnings = r.ReadSingle(); Retired = r.ReadBool(); Position = r.ReadVector3(); Rotation = r.ReadQuaternion();
        }
    }
}
