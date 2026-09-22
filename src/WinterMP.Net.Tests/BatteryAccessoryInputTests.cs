using System;
using System.IO;
using System.Text.Json.Nodes;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class BatteryAccessoryInputTests
    {
        [Theory]
        [InlineData(-.5f)] [InlineData(0)] [InlineData(126)] [InlineData(147.25f)] [InlineData(float.MaxValue)]
        public void FullWidthNativeMaximumIsAppendedAfterCharge(float maximum)
        {
            var source = new BatteryState { Revision = 17, Flags = 3, Charge = 99.25f, ChargeMax = maximum };
            byte[] packet = PacketCodec.Encode(source); Assert.Equal(15, packet.Length);
            var state = Assert.IsType<BatteryState>(PacketCodec.Decode(packet));
            Assert.Equal(17u, state.Revision); Assert.Equal((byte)3, state.Flags);
            Assert.Equal(99.25f, state.Charge); Assert.Equal(maximum, state.ChargeMax);
            var reader = new NetReader(packet); Assert.Equal((ushort)MessageId.BatteryState, reader.ReadUInt16());
            Assert.Equal(17u, reader.ReadUInt32()); Assert.Equal((byte)3, reader.ReadByte());
            Assert.Equal(99.25f, reader.ReadSingle()); Assert.Equal(maximum, reader.ReadSingle());
        }

        [Theory]
        [InlineData(3, float.NaN)] [InlineData(3, float.PositiveInfinity)] [InlineData(3, float.NegativeInfinity)]
        [InlineData(0, 126)] [InlineData(1, 147)]
        public void InvalidOrUnavailableMaximumIsRejectedAtEveryBoundary(byte flags, float maximum)
        {
            var state = new BatteryState { Flags = flags, ChargeMax = maximum };
            Assert.False(state.Valid); Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            var w = new NetWriter(); w.WriteUInt32(0); w.WriteByte(flags); w.WriteSingle(0); w.WriteSingle(maximum);
            Assert.Throws<ProtocolException>(() => new BatteryState().Read(new NetReader(w.ToArray())));
            Assert.False(new BatteryReplica().Receive(state));
            Assert.Throws<ArgumentException>(() => new BatteryPublication().Observe(flags, 0, maximum));
        }

        [Fact]
        public void PreviousLayoutCannotDecodeWithoutTheNewMaximum()
        {
            var w = new NetWriter(); w.WriteUInt16((ushort)MessageId.BatteryState); w.WriteUInt32(1); w.WriteByte(3); w.WriteSingle(126);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(w.ToArray()));
        }

        [Fact]
        public void MaximumOnlyChangesAreCopiedRevisionedAndCannotBeLostBySnapshots()
        {
            var publication = new BatteryPublication(); var replica = new BatteryReplica();
            var first = publication.Observe(3, 100, 126); publication.MarkBroadcast(first.Revision); Assert.True(replica.Receive(first));
            first.ChargeMax = 1; Assert.Equal(126, replica.Get()!.ChargeMax);
            var snapshot = publication.Observe(3, 100, 147); Assert.Equal(first.Revision + 1, snapshot.Revision);
            snapshot.ChargeMax = 0; var live = publication.Observe(3, 100, 147);
            Assert.Equal(snapshot.Revision, live.Revision); Assert.Equal(147, live.ChargeMax);
            publication.MarkBroadcast(first.Revision); Assert.True(publication.NeedsBroadcast);
            Assert.True(replica.Receive(live)); var conflict = live.Copy(); conflict.ChargeMax = 126;
            Assert.False(replica.Receive(conflict)); var copy = replica.Get()!; copy.ChargeMax = 0;
            Assert.Equal(147, replica.Get()!.ChargeMax); publication.MarkBroadcast(live.Revision); Assert.False(publication.NeedsBroadcast);
            var removed = publication.Observe(1, 0, 0); Assert.True(replica.Receive(removed)); Assert.Equal(0, replica.Get()!.ChargeMax);
            replica.Clear(); Assert.Null(replica.Get());
        }

        [Theory]
        [InlineData(null)] [InlineData("[]")] [InlineData("null")] [InlineData("{}")] [InlineData("[\"Charge\"]")]
        [InlineData("[\"Installed\",\"Charge\",\"Charge\"]")]
        [InlineData("[\"Installed\",\"Charge\",\"Amps\"]")]
        [InlineData("[\"Installed\",\"Charge\",\"ChargeMax\",\"Amps\"]")]
        public void BatteryProjectionRequiresAllThreePublishedFields(string? value)
        {
            var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sync-catalog.json")))!;
            var battery = json["guestEngineInputs"]!["battery"]!.AsObject();
            if (value == null) battery.Remove("readVariables"); else battery["readVariables"] = JsonNode.Parse(value);
            var parsed = SyncCatalogJson.Parse(json.ToJsonString());
            Assert.Null(parsed.GuestEngineInputs); Assert.NotNull(parsed.GuestEngineInputsError);
            Assert.NotNull(parsed.GuestEngineProtection); Assert.NotNull(parsed.VehicleElectrical); Assert.NotEmpty(parsed.Doors);
        }
    }
}
