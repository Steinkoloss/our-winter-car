using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class TaxiLuggageTests
    {
        [Theory]
        [InlineData(6, 5, 5)] [InlineData(3, 5, 3)] [InlineData(0, 5, 0)] [InlineData(2, 0, 0)] [InlineData(-1, 5, 0)]
        public void NativeDrawCannotRequestMoreThanAvailableChoices(int requested, int available, int expected)
            => Assert.Equal(expected, TaxiServicePolicy.BoundLuggageCount(requested, available));
        [Fact]
        public void EverySlotAndResetHasADistinctIdentity()
        {
            var ids = new HashSet<uint>();
            foreach (uint epoch in new[] { 1u, 2u, 3u, uint.MaxValue })
                for (int i = 0; i < 5; i++) Assert.True(ids.Add(TaxiServicePolicy.LuggageItemId(epoch, i)));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaxiServicePolicy.LuggageItemId(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaxiServicePolicy.LuggageItemId(1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => TaxiServicePolicy.LuggageItemId(1, 5));
        }
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(15)] [InlineData(31)]
        public void HiddenOrSelectedLuggageAndCurrentWorldPosesRoundTrip(byte mask)
        {
            var s = new TaxiServiceState { LuggageEpoch = 31, LuggageMask = mask };
            for (int i = 0; i < 5; i++) { s.LuggagePositions[i] = new NetVector3(i, 3, -4); s.LuggageRotations[i] = new NetQuaternion(0, 1, 0, 0); }
            byte[] bytes = PacketCodec.Encode(s); var copy = Assert.IsType<TaxiServiceState>(PacketCodec.Decode(bytes));
            Assert.Equal(bytes, PacketCodec.Encode(copy)); Assert.Equal(mask, copy.LuggageMask); Assert.Equal(31u, copy.LuggageEpoch);
            for (int i = 0; i < 5; i++) { Assert.Equal((float)i, copy.LuggagePositions[i].X); Assert.Equal(1f, copy.LuggageRotations[i].Y); }
        }
        [Theory]
        [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
        public void InvalidLuggagePoseCannotReachUnity(float value)
        {
            var s = new TaxiServiceState(); s.LuggagePositions[4].X = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            s = new TaxiServiceState(); s.LuggageRotations[0].W = value;
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(s));
            byte[] bytes = PacketCodec.Encode(new TaxiServiceState());
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, bytes.Length - 37 - 4, 4);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
        [Fact]
        public void BadEpochMaskAndArrayShapeAreRejected()
        {
            var s = new TaxiServiceState { LuggageEpoch = 0 }; Assert.False(TaxiServicePolicy.Valid(s));
            s = new TaxiServiceState { LuggageMask = 32 }; Assert.False(TaxiServicePolicy.Valid(s));
            s = new TaxiServiceState { LuggagePositions = new NetVector3[4] }; Assert.False(TaxiServicePolicy.Valid(s));
            s = new TaxiServiceState { LuggageRotations = new NetQuaternion[6] }; Assert.False(TaxiServicePolicy.Valid(s));
            s = new TaxiServiceState(); s.LuggageRotations[2] = new NetQuaternion(); Assert.False(TaxiServicePolicy.Valid(s));
            var bytes = PacketCodec.Encode(new TaxiServiceState()); bytes[bytes.Length - 37 - 141] = 128;
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes));
        }
    }
}
