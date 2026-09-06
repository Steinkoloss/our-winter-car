namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Retired layout: slots moved to SlotMachineState in v92 and Ventti observations
    /// to VenttiTableState in v98. Kept decodable for diagnostics; no current sender
    /// or game-state handler uses message 93. Its id and fields must never be reused.
    /// </summary>
    public sealed class GamblingState : IMessage
    {
        public const byte KindSlot = 0; // retired; never reuse
        public const byte KindVentti = 1;

        public const byte FlagReel1Locked = 1;
        public const byte FlagReel2Locked = 2;
        public const byte FlagReel3Locked = 4;
        /// <summary>Retired active flag (legacy Ventti actually sent its car-wager flag).</summary>
        public const byte FlagActive = 8;

        /// <summary>Stable scene-path hash of the device container (see PLAN §4.1).</summary>
        public uint MachineId;
        public byte Kind;
        public byte Flags;
        /// <summary>Retired credit/stake field; legacy Ventti truncated it to a byte.</summary>
        public float Credit;
        /// <summary>Retired byte stake.</summary>
        public byte Bet;
        /// <summary>Ventti player hand total.</summary>
        public byte V1;
        /// <summary>Ventti house hand total.</summary>
        public byte V2;
        /// <summary>Retired reserved outcome; legacy senders always wrote zero.</summary>
        public byte V3;
        /// <summary>Last resolved winnings, mk (negative for a net loss on a resolved hand).</summary>
        public int Payout;

        public MessageId Id => MessageId.GamblingState;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(MachineId);
            writer.WriteByte(Kind);
            writer.WriteByte(Flags);
            writer.WriteSingle(Credit);
            writer.WriteByte(Bet);
            writer.WriteByte(V1);
            writer.WriteByte(V2);
            writer.WriteByte(V3);
            writer.WriteInt32(Payout);
        }

        public void Read(NetReader reader)
        {
            MachineId = reader.ReadUInt32();
            Kind = reader.ReadByte();
            Flags = reader.ReadByte();
            Credit = reader.ReadSingle();
            Bet = reader.ReadByte();
            V1 = reader.ReadByte();
            V2 = reader.ReadByte();
            V3 = reader.ReadByte();
            Payout = reader.ReadInt32();
        }
    }

    /// <summary>
    /// Retired in v99; retained for diagnostics only. Slots use 165, Ventti uses 176.
    /// </summary>
    public sealed class GamblingIntent : IMessage
    {
        // Retired slot actions, reserved permanently. No v92 sender emits these.
        public const byte ActionPay = 0;
        public const byte ActionBet = 1;
        public const byte ActionStart = 2;
        public const byte ActionLock1 = 3;
        public const byte ActionLock2 = 4;
        public const byte ActionLock3 = 5;
        public const byte ActionCashout = 6;
        // Retired Ventti actions; these numbers remain reserved.
        public const byte ActionVenttiBet = 7;
        public const byte ActionVenttiDeal = 8;
        public const byte ActionVenttiHit = 9;
        public const byte ActionVenttiStand = 10;
        public const byte ActionVenttiWagerCar = 11;

        public uint MachineId;
        public byte Action;
        public byte PlayerId;
        public ushort Sequence;

        public MessageId Id => MessageId.GamblingIntent;

        public void Write(NetWriter writer)
        {
            writer.WriteUInt32(MachineId);
            writer.WriteByte(Action);
            writer.WriteByte(PlayerId);
            writer.WriteUInt16(Sequence);
        }

        public void Read(NetReader reader)
        {
            MachineId = reader.ReadUInt32();
            Action = reader.ReadByte();
            PlayerId = reader.ReadByte();
            Sequence = reader.ReadUInt16();
        }
    }
}
