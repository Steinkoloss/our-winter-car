namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> guests: legacy Ventti display state. Guest resolvers stay suppressed;
    /// property transfers and inactive-host play remain incomplete (coverage R2.11–13).
    /// Slot use of this layout is retired in v92; see SlotMachineState.
    /// </summary>
    public sealed class GamblingState : IMessage
    {
        public const byte KindSlot = 0; // retired; never reuse
        public const byte KindVentti = 1;

        public const byte FlagReel1Locked = 1;
        public const byte FlagReel2Locked = 2;
        public const byte FlagReel3Locked = 4;
        /// <summary>A Ventti hand is live.</summary>
        public const byte FlagActive = 8;

        /// <summary>Stable scene-path hash of the device container (see PLAN §4.1).</summary>
        public uint MachineId;
        public byte Kind;
        public byte Flags;
        /// <summary>Ventti stake; currently clamped to a byte before assignment (R2.13).</summary>
        public float Credit;
        /// <summary>Ventti stake clamped to a byte.</summary>
        public byte Bet;
        /// <summary>Ventti player hand total.</summary>
        public byte V1;
        /// <summary>Ventti house hand total.</summary>
        public byte V2;
        /// <summary>Reserved Ventti outcome; currently always zero.</summary>
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
    /// Guest -> host: legacy Ventti controls. Validated intents replay a host FSM;
    /// inactive table settlement remains R2.12. Slots use SlotMachineIntent in v92.
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
        // Ventti table actions (reuse the same wire; see VenttiSync).
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
