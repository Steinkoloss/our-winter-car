using System;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed partial class BeerCaseTests
    {
        // This valid non-owner extraction envelope must reach an authority instead
        // of falling back to relative FSM Remove bottle replay. Fixture counts are
        // deliberately not a claim about vanilla capacity or saved bottle IDs.
        [Fact]
        public void NonOwnerExtractionAndAbsoluteResultAreDecodableWithStableCaseIdentity()
        {
            uint id = StableHash.Fnv1a32("beercase:fixture-native-id");
            var w = new NetWriter();
            w.WriteUInt16(265); w.WriteUInt32(id); w.WriteString("fixture-native-id");
            w.WriteUInt32(7); w.WriteUInt32(12); w.WriteUInt32(1); w.WriteUInt32(1);
            w.WriteInt32(3); w.WriteByte(2);
            byte[] request = w.ToArray();
            Assert.Equal(request, PacketCodec.Encode(PacketCodec.Decode(request)));
            w = new NetWriter();
            w.WriteUInt16(266); w.WriteUInt32(id); w.WriteString("fixture-native-id");
            w.WriteUInt32(7); w.WriteUInt32(2); w.WriteInt32(3); w.WriteInt32(2);
            w.WriteByte(1); w.WriteByte(2); w.WriteUInt32(12); w.WriteUInt32(1);
            byte[] result = w.ToArray();
            Assert.Equal(result, PacketCodec.Encode(PacketCodec.Decode(result)));
        }
    }
}
