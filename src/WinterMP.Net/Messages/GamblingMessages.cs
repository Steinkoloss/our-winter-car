namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> all: authoritative state of a shared gambling device (pub/station slot
    /// machine, Ventti blackjack table). The stake, the RNG result and the payout all
    /// mutate the single shared wallet, so the host owns them: it runs the spin/deal and
    /// broadcasts the resolved reels/hand + credit + payout; guests display these and let
    /// the wallet ride the normal <see cref="WalletState"/> stream (a guest win therefore
    /// survives the next WalletState instead of flip-flopping). See GamblingSync/VenttiSync.
    /// </summary>
    public sealed class GamblingState : IMessage
    {
        public const byte KindSlot = 0;
        public const byte KindVentti = 1;

        public const byte FlagReel1Locked = 1;
        public const byte FlagReel2Locked = 2;
        public const byte FlagReel3Locked = 4;
        /// <summary>A spin/hand is in progress (slot spinning, or a Ventti hand is live).</summary>
        public const byte FlagActive = 8;

        /// <summary>Stable scene-path hash of the device container (see PLAN §4.1).</summary>
        public uint MachineId;
        public byte Kind;
        public byte Flags;
        /// <summary>Slot: inserted credit. Ventti: current bet on the table.</summary>
        public float Credit;
        /// <summary>Slot: bet level 1..5. Ventti: bet step index.</summary>
        public byte Bet;
        /// <summary>Slot: reel-1 symbol. Ventti: player hand total.</summary>
        public byte V1;
        /// <summary>Slot: reel-2 symbol. Ventti: house hand total.</summary>
        public byte V2;
        /// <summary>Slot: reel-3 symbol. Ventti: outcome code (0 none/1 player/2 house/3 push).</summary>
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
    /// Guest -> host: an anyone-triggers action on a shared gambling device. The host
    /// validates id + fresh nearby pose + monotonic sequence, forces the matching button
    /// state on its authoritative FSM (via <c>FsmHook.FireRemoteEntry</c>) so the real
    /// stake/RNG/payout runs host-side, and the resulting <see cref="GamblingState"/> +
    /// <see cref="WalletState"/> carry the outcome back.
    /// </summary>
    public sealed class GamblingIntent : IMessage
    {
        // Slot machine actions.
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
