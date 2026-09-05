namespace WinterMP.Net.Messages
{
    public sealed class SlotMachineIntent : IMessage
    {
        public const byte Pay = 0, Bet = 1, Spin = 2, Hold1 = 3, Hold2 = 4, Hold3 = 5, Cashout = 6, Finish = 7;
        public uint MachineId;
        public byte PlayerId;
        public ushort Sequence;
        public byte Action;
        public uint Round;
        public MessageId Id => MessageId.SlotMachineIntent;
        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(MachineId); writer.WriteByte(PlayerId); writer.WriteUInt16(Sequence);
            writer.WriteByte(Action); writer.WriteUInt32(Round);
        }
        public void Read(NetReader reader)
        {
            MachineId = reader.ReadUInt32(); PlayerId = reader.ReadByte(); Sequence = reader.ReadUInt16();
            Action = reader.ReadByte(); Round = reader.ReadUInt32();
        }
    }

    public sealed class SlotMachineState : IMessage
    {
        public const byte NoPlayer = 255;
        public uint MachineId, Revision, Round;
        public byte PlayerId = NoPlayer;
        public bool Spinning;
        public byte Bet = 1, HoldMask;
        public bool CanHold;
        public int Credit, Winnings, LastWin;
        public byte Reel1, Reel2, Reel3;
        public MessageId Id => MessageId.SlotMachineState;
        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(MachineId); writer.WriteUInt32(Revision); writer.WriteUInt32(Round);
            writer.WriteByte(PlayerId); writer.WriteBool(Spinning); writer.WriteByte(Bet);
            writer.WriteByte(HoldMask); writer.WriteBool(CanHold);
            writer.WriteInt32(Credit); writer.WriteInt32(Winnings); writer.WriteInt32(LastWin);
            writer.WriteByte(Reel1); writer.WriteByte(Reel2); writer.WriteByte(Reel3);
        }
        public void Read(NetReader reader)
        {
            MachineId = reader.ReadUInt32(); Revision = reader.ReadUInt32(); Round = reader.ReadUInt32();
            PlayerId = reader.ReadByte(); Spinning = reader.ReadBool(); Bet = reader.ReadByte();
            HoldMask = reader.ReadByte(); CanHold = reader.ReadBool();
            Credit = reader.ReadInt32(); Winnings = reader.ReadInt32(); LastWin = reader.ReadInt32();
            Reel1 = reader.ReadByte(); Reel2 = reader.ReadByte(); Reel3 = reader.ReadByte();
        }
    }

    public sealed class SlotMachineResult : IMessage
    {
        public const byte Accepted = 0, Busy = 1, Funds = 2, Invalid = 3, Distant = 4;
        // Internal transient/stale outcomes are never acknowledged as completed requests.
        public const byte Retry = 254, Stale = 255;
        public uint MachineId;
        public byte PlayerId;
        public ushort Sequence;
        public byte Result;
        public int CashDelta;
        public MessageId Id => MessageId.SlotMachineResult;
        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(MachineId); writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence); writer.WriteByte(Result);
            writer.WriteInt32(CashDelta);
        }
        public void Read(NetReader reader)
        {
            MachineId = reader.ReadUInt32(); PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16(); Result = reader.ReadByte();
            CashDelta = reader.ReadInt32();
        }
    }
}
