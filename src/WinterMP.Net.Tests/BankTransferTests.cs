using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class BankTransferTests
    {
        [Theory]
        [InlineData(100, 900f, 2100f)]
        [InlineData(-100, 1100f, 1900f)]
        [InlineData(-200, 1200f, 1800f)]
        [InlineData(-300, 1300f, 1700f)]
        [InlineData(-500, 1500f, 1500f)]
        [InlineData(-800, 1800f, 1200f)]
        [InlineData(-1000, 2000f, 1000f)]
        public void VanillaDenominationsMoveFundsWithoutChangingTotal(short amount, float cash, float bank)
        {
            Assert.True(BankTransferPolicy.TryTransfer(1000f, 2000f, amount, out var nextCash, out var nextBank));
            Assert.Equal(cash, nextCash);
            Assert.Equal(bank, nextBank);
            Assert.Equal(3000f, nextCash + nextBank);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(200)]
        [InlineData(-400)]
        [InlineData(short.MinValue)]
        [InlineData(short.MaxValue)]
        public void UndefinedDenominationsDoNotChangeEitherBalance(short amount)
        {
            Assert.False(BankTransferPolicy.TryTransfer(1000f, 2000f, amount, out var cash, out var bank));
            Assert.Equal(1000f, cash);
            Assert.Equal(2000f, bank);
        }

        [Theory]
        [InlineData(99f, 1000f, 100)]
        [InlineData(1000f, 99f, -100)]
        [InlineData(float.NaN, 1000f, 100)]
        [InlineData(1000f, float.PositiveInfinity, -100)]
        [InlineData(float.NegativeInfinity, 1000f, 100)]
        [InlineData(1000f, 1e20f, 100)]
        public void InsufficientInvalidOrUnrepresentableBalancesAreRejected(float cash, float bank, short amount)
        {
            Assert.False(BankTransferPolicy.TryTransfer(cash, bank, amount, out _, out _));
        }

        [Fact]
        public void DepositCanPayDownAnOverdrawnAccount()
        {
            Assert.True(BankTransferPolicy.TryTransfer(100f, -250f, 100, out var cash, out var bank));
            Assert.Equal(0f, cash);
            Assert.Equal(-150f, bank);
        }

        [Fact]
        public void DefaultGameBalancesCanCrossAFloatPrecisionBoundary()
        {
            Assert.True(BankTransferPolicy.TryTransfer(2140f, 2043.58f, 100, out var cash, out var bank));
            Assert.Equal(2040f, cash);
            Assert.InRange(bank, 2143.575f, 2143.585f);
        }

        [Fact]
        public void ReceiptSurvivesRetryAndDoesNotAuthorizeAnotherSettlement()
        {
            var ledger = new BankTransferLedger();
            Assert.True(ledger.IsNew(1, 40000));
            ledger.Record(1, 40000, true);
            Assert.False(ledger.IsNew(1, 40000));
            Assert.True(ledger.TryGetReceipt(1, 40000, out bool accepted));
            Assert.True(accepted);
            Assert.False(ledger.IsNew(1, 39999));
            Assert.True(ledger.IsNew(1, 40001));
            Assert.True(ledger.IsNew(2, 1));
        }

        [Fact]
        public void TerminalRejectionCannotBecomeAcceptedAfterFundsArrive()
        {
            var ledger = new BankTransferLedger();
            ledger.Record(1, 1, false);
            Assert.False(ledger.IsNew(1, 1));
            Assert.True(ledger.TryGetReceipt(1, 1, out bool accepted));
            Assert.False(accepted);
        }

        [Fact]
        public void SequenceWrapAndReconnectAreIndependentOfOtherPlayers()
        {
            var ledger = new BankTransferLedger();
            ledger.Record(1, ushort.MaxValue, true);
            ledger.Record(2, 12, true);
            Assert.True(ledger.IsNew(1, 0));
            ledger.Record(1, 0, true);
            Assert.False(ledger.IsNew(1, ushort.MaxValue));
            ledger.ForgetPlayer(1);
            Assert.True(ledger.IsNew(1, 1));
            Assert.False(ledger.IsNew(2, 1));
        }

        [Fact]
        public void EconomyMessagesPreserveNegativeBalancesAndSignedTransfers()
        {
            var wallet = (WalletState)PacketCodec.Decode(PacketCodec.Encode(new WalletState
            {
                Money = 125.25f, Sequence = ushort.MaxValue, BankBalance = -50.5f,
                NetIncome = 1234.5f, Flags = WalletState.FlagBankBalance | WalletState.FlagNetIncome,
            }));
            Assert.Equal(125.25f, wallet.Money);
            Assert.Equal(-50.5f, wallet.BankBalance);
            Assert.Equal(1234.5f, wallet.NetIncome);
            Assert.Equal(3, wallet.Flags);
            Assert.Equal(ushort.MaxValue, wallet.Sequence);
            var request = (BankTransferIntent)PacketCodec.Decode(PacketCodec.Encode(new BankTransferIntent
            {
                PlayerId = 3, Sequence = 0, Amount = -1000,
            }));
            Assert.Equal(-1000, request.Amount);
            Assert.Equal(3, request.PlayerId);
            Assert.Equal(0, request.Sequence);
            Assert.True(SessionMessagePolicy.IsChannelAllowed(request.Id, Channel.ReliableOrdered));
            Assert.False(SessionMessagePolicy.IsChannelAllowed(request.Id, Channel.UnreliableSequenced));
        }
    }
}
