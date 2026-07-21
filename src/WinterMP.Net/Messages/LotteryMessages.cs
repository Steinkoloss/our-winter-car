namespace WinterMP.Net.Messages
{
    /// <summary>
    /// Host -> all: the national lottery draw (Lotto / Megaveto). The draw RNG runs
    /// per-client, so without this each peer rolls different winning numbers and a ticket
    /// that wins on one loses on the other. The <b>host</b> owns the draw and broadcasts the
    /// round + winning line + national pot on change + join; guests write it onto their local
    /// Lottery FSM so every ticket is judged against the same numbers. Ticket buy-in routes
    /// through the host purchase path (the ticket Pay buttons are catalogued buys). See
    /// LotterySync.
    /// </summary>
    public sealed class LotteryDrawState : IMessage
    {
        /// <summary>The current round's draw has been rolled (host <c>DrawDone</c>).</summary>
        public const byte FlagDrawDone = 1;

        public int Round;
        public int NationalPot;
        public string WinningNumbers = string.Empty;
        public byte Flags;

        public bool DrawDone => (Flags & FlagDrawDone) != 0;

        public MessageId Id => MessageId.LotteryDrawState;

        public void Write(NetWriter writer)
        {
            writer.WriteInt32(Round);
            writer.WriteInt32(NationalPot);
            writer.WriteString(WinningNumbers);
            writer.WriteByte(Flags);
        }

        public void Read(NetReader reader)
        {
            Round = reader.ReadInt32();
            NationalPot = reader.ReadInt32();
            WinningNumbers = reader.ReadString();
            Flags = reader.ReadByte();
        }
    }
}
