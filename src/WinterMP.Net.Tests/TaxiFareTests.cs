using System;
using System.IO;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TaxiFareTests
    {
        private static TaxiFareState Quote() => new TaxiFareState { FareId = 4, ControlRevision = 9, Revision = 20,
            Flags = TaxiFareState.Active | TaxiFareState.Arrived | TaxiFareState.CanCharge, QuotedCost = 23.483f,
            TerminalDisplay = "23.5", OfferLabel = "23.5 MK" };
        private static TaxiFareIntent Intent(TaxiFareAction action = TaxiFareAction.Charge) => new TaxiFareIntent { FareId = 4,
            ExpectedControlRevision = 9, Sequence = 8, PlayerId = 1, Action = action };
        private static TaxiFareState Offer()
        {
            var s = Quote(); s.Flags = TaxiFareState.Active | TaxiFareState.Charged | TaxiFareState.CashVisible | TaxiFareState.CanCollect;
            s.OfferedCost = s.QuotedCost; return s;
        }
        [Fact]
        public void QuoteCollectionAndDelayedDuplicatesCannotRepeatIncome()
        {
            var s = Quote(); var quote = Intent(); Assert.True(TaxiFarePolicy.CanAct(s, quote, 1, true));
            s = Offer(); s.ControlRevision++;
            Assert.False(TaxiFarePolicy.CanAct(s, quote, 1, true));
            var collect = Intent(TaxiFareAction.Collect); collect.ExpectedControlRevision = s.ControlRevision;
            Assert.True(TaxiFarePolicy.CanAct(s, collect, 1, true));
            s.Flags = TaxiFareState.Active | TaxiFareState.Charged | TaxiFareState.Paid; s.ControlRevision++;
            Assert.False(TaxiFarePolicy.CanAct(s, collect, 1, true));
            collect.ExpectedControlRevision = s.ControlRevision;
            Assert.False(TaxiFarePolicy.CanAct(s, collect, 1, true));
        }
        [Theory]
        [InlineData(TaxiFareAction.Charge)] [InlineData(TaxiFareAction.Collect)]
        public void ControlsRequireCurrentFareRevisionAuthenticatedActorAndFreshNearPose(TaxiFareAction action)
        {
            var s = action == TaxiFareAction.Charge ? Quote() : Offer(); var i = Intent(action);
            Assert.True(TaxiFarePolicy.CanAct(s, i, 1, true));
            Assert.False(TaxiFarePolicy.CanAct(s, i, 2, true)); Assert.False(TaxiFarePolicy.CanAct(s, i, 1, false));
            s.ControlRevision++; Assert.False(TaxiFarePolicy.CanAct(s, i, 1, true));
            s.ControlRevision--; s.FareId++; Assert.False(TaxiFarePolicy.CanAct(s, i, 1, true));
            s.FareId--; i.Sequence = 0; Assert.False(TaxiFarePolicy.CanAct(s, i, 1, true));
        }
        [Fact]
        public void AnUnavailablePhaseNeverAcceptsTheOtherAction()
        {
            Assert.False(TaxiFarePolicy.CanAct(Quote(), Intent(TaxiFareAction.Collect), 1, true));
            Assert.False(TaxiFarePolicy.CanAct(Offer(), Intent(), 1, true));
            var s = Quote(); s.Flags = TaxiFareState.Active; Assert.False(TaxiFarePolicy.CanAct(s, Intent(), 1, true));
            s.FareId = 0; Assert.False(TaxiFarePolicy.CanAct(s, Intent(), 1, true));
        }
        [Fact]
        public void DisplayRevisionsDoNotInvalidateControls()
        {
            var s = Quote(); s.Revision++; s.TerminalDisplay = "32.0";
            Assert.True(TaxiFarePolicy.CanAct(s, Intent(), 1, true));
        }
        [Fact]
        public void WireRoundTripPreservesTheHostFloatRatherThanRoundedCashLabel()
        {
            var s = Offer(); var bytes = PacketCodec.Encode(s); var copy = Assert.IsType<TaxiFareState>(PacketCodec.Decode(bytes));
            Assert.Equal(bytes, PacketCodec.Encode(copy)); Assert.Equal(23.483f, copy.OfferedCost); Assert.Equal("23.5 MK", copy.OfferLabel);
            foreach (var action in new[] { TaxiFareAction.Charge, TaxiFareAction.Collect })
            { var i = Intent(action); Assert.Equal(action, Assert.IsType<TaxiFareIntent>(PacketCodec.Decode(PacketCodec.Encode(i))).Action); }
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)] [InlineData(-1f)] [InlineData(10000001f)]
        public void InvalidAmountsAreRejectedOnReadAndWrite(float invalid)
        {
            var s = Quote(); var bytes = PacketCodec.Encode(s); s.QuotedCost = invalid;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            Array.Copy(BitConverter.GetBytes(invalid), 0, bytes, 15, 4); Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
            s = Offer(); s.OfferedCost = invalid; Assert.False(TaxiFarePolicy.Valid(s));
        }
        [Theory]
        [InlineData(TaxiFareState.CanCharge)] [InlineData(TaxiFareState.CanCollect)]
        [InlineData(TaxiFareState.Active | TaxiFareState.CanCharge | TaxiFareState.Charged)]
        [InlineData(TaxiFareState.Active | TaxiFareState.CanCollect | TaxiFareState.CashVisible | TaxiFareState.Charged | TaxiFareState.Paid)]
        public void ContradictoryActionFlagsCannotBecomeGuestInput(byte flags)
        { var s = Quote(); s.Flags = flags; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s)); }
        [Fact]
        public void MissingFareBadTextAndInvalidIntentsAreContained()
        {
            var s = Quote(); s.FareId = 0; Assert.False(TaxiFarePolicy.Valid(s));
            s = Quote(); s.OfferLabel = "a\0b"; Assert.False(TaxiFarePolicy.Valid(s));
            s = Quote(); s.TerminalDisplay = new string('a', 129); Assert.False(TaxiFarePolicy.Valid(s));
            var i = Intent(); i.FareId = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(i));
            i = Intent(); i.PlayerId = 255; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(i));
            i = Intent(); i.Sequence = 0; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(i));
            i = Intent(); i.Action = (TaxiFareAction)5; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(i));
        }
        [Fact]
        public void OnlyAdmittedHostStateAndAuthenticatedGuestIntentsUseOrderedChannel()
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareState, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareState, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareState, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareState, false, true, true, false));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareIntent, false, true, true, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareIntent, true, false, false, true));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(MessageId.TaxiFareIntent, true, true, false, true));
            foreach (var id in new[] { MessageId.TaxiFareState, MessageId.TaxiFareIntent })
            {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            }
        }
        [Theory]
        [InlineData(TaxiFareAction.PrintReceipt, TaxiReceiptStage.Hidden, TaxiFareState.CanPrint)]
        [InlineData(TaxiFareAction.TakeReceipt, TaxiReceiptStage.Ready, TaxiFareState.CanTake)]
        [InlineData(TaxiFareAction.GiveReceipt, TaxiReceiptStage.Loose, TaxiFareState.CanGive)]
        public void ReceiptActionsRequireMatchingPhaseAndCurrentFare(TaxiFareAction action, TaxiReceiptStage stage, byte flags)
        {
            var s = Offer(); s.Flags |= TaxiFareState.Paid | TaxiFareState.ReceiptRequested;
            s.Flags &= unchecked((byte)~TaxiFareState.CanCollect); s.ReceiptStage = stage; s.ReceiptFlags = flags;
            var i = Intent(action); Assert.True(TaxiFarePolicy.CanAct(s, i, 1, true));
            Assert.False(TaxiFarePolicy.CanAct(s, i, 2, true)); Assert.False(TaxiFarePolicy.CanAct(s, i, 1, false));
            var copy = Assert.IsType<TaxiFareState>(PacketCodec.Decode(PacketCodec.Encode(s)));
            Assert.Equal(stage, copy.ReceiptStage); Assert.Equal(flags, copy.ReceiptFlags);
            Assert.Equal(action, Assert.IsType<TaxiFareIntent>(PacketCodec.Decode(PacketCodec.Encode(i))).Action);
            s.ControlRevision++; Assert.False(TaxiFarePolicy.CanAct(s, i, 1, true));
            i.ExpectedControlRevision++; s.ReceiptFlags = 0; Assert.False(TaxiFarePolicy.CanAct(s, i, 1, true));
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void ReceiptPoseRejectsInvalidWireCoordinates(float invalid)
        {
            var s = Quote(); s.ReceiptPosition.X = invalid; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Quote(); s.ReceiptRotation.W = invalid; Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = Quote(); var bytes = PacketCodec.Encode(s); Array.Copy(BitConverter.GetBytes(invalid), 0, bytes, bytes.Length - 4, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void ReceiptFlagsRejectMissingFareWrongPhaseAndUnpaidHandoff()
        {
            var s = Quote(); s.ReceiptFlags = TaxiFareState.CanPrint; Assert.False(TaxiFarePolicy.Valid(s));
            s = Offer(); s.ReceiptFlags = TaxiFareState.CanTake; Assert.False(TaxiFarePolicy.Valid(s));
            s.ReceiptStage = TaxiReceiptStage.Ready; Assert.True(TaxiFarePolicy.Valid(s));
            s.ReceiptStage = TaxiReceiptStage.Loose; s.ReceiptFlags = TaxiFareState.CanGive; Assert.False(TaxiFarePolicy.Valid(s));
            s = Quote(); s.ReceiptRotation = new NetQuaternion(); Assert.False(TaxiFarePolicy.Valid(s));
            s = Quote(); s.ReceiptFlags = 128; Assert.False(TaxiFarePolicy.Valid(s));
            s = Quote(); s.ReceiptStage = (TaxiReceiptStage)6; Assert.False(TaxiFarePolicy.Valid(s));
        }
        [Fact]
        public void CatalogKeepsStableNativeCustomerAndContainsMissingFields()
        {
            var d = SyncCatalogJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")));
            Assert.Null(d.TaxiFareError); Assert.NotNull(d.TaxiFare); Assert.Equal("Customer", d.TaxiFare!["customerVariable"]);
            Assert.Equal("Make payment", d.TaxiFare["chargeState"]);
            d = SyncCatalogJson.Parse("{\"taxiFare\":{}}"); Assert.Null(d.TaxiFare); Assert.NotNull(d.TaxiFareError);
        }
    }
}
