using System;

namespace WinterMP.Net
{
    /// <summary>Installed-game rules; card ids index the native Values/Textures arrays.</summary>
    public sealed class VenttiRules
    {
        private readonly int[] _cardValues;
        public float BetIncrement { get; private set; }
        public float PropertyThreshold { get; private set; }
        public float WinLimitIncrease { get; private set; }
        public float OpponentLossLimit { get; private set; }
        public float PropertyWinLoss { get; private set; }
        public float PropertyLoseLoss { get; private set; }

        public VenttiRules(int[] cardValues, float betIncrement, float propertyThreshold,
            float winLimitIncrease, float opponentLossLimit, float propertyWinLoss, float propertyLoseLoss)
        {
            if (cardValues == null || cardValues.Length != 53 || cardValues[0] != 0)
                throw new ArgumentException("Ventti requires 52 cards and the unused zero slot.");
            for (int i = 1; i < cardValues.Length; i++)
                if (cardValues[i] < 1 || cardValues[i] > 13) throw new ArgumentException("Invalid Ventti card value.");
            if (!Positive(betIncrement) || !Positive(propertyThreshold) || !Positive(winLimitIncrease)
                || !Positive(opponentLossLimit) || !Nonnegative(propertyWinLoss) || !Nonnegative(propertyLoseLoss)
                || !Positive(propertyThreshold + betIncrement))
                throw new ArgumentException("Invalid Ventti betting rules.");
            _cardValues = (int[])cardValues.Clone();
            BetIncrement = betIncrement; PropertyThreshold = propertyThreshold;
            WinLimitIncrease = winLimitIncrease; OpponentLossLimit = opponentLossLimit;
            PropertyWinLoss = propertyWinLoss; PropertyLoseLoss = propertyLoseLoss;
        }

        public int CardValue(byte card)
        {
            if (card < 1 || card > 52) throw new ArgumentOutOfRangeException(nameof(card));
            return _cardValues[card];
        }

        internal static bool Nonnegative(float value) => BankTransferPolicy.IsFinite(value) && value >= 0;
        private static bool Positive(float value) => BankTransferPolicy.IsFinite(value) && value > 0;
    }
}
