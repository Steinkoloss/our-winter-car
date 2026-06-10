using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class WorldMessagesTests
    {
        [Fact]
        public void FsmStateEnter_RoundTrips()
        {
            var original = new FsmStateEnter { NetId = 0x87654321, StateName = "Open door" };
            var decoded = Assert.IsType<FsmStateEnter>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NetId, decoded.NetId);
            Assert.Equal(original.StateName, decoded.StateName);
        }

        [Fact]
        public void FsmRawEvent_RoundTrips()
        {
            var original = new FsmRawEvent { NetId = 0xDEADBEEF, EventName = "TIGHTEN" };
            var decoded = Assert.IsType<FsmRawEvent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NetId, decoded.NetId);
            Assert.Equal(original.EventName, decoded.EventName);
        }

        [Fact]
        public void ItemTransform_RoundTrips()
        {
            var original = new ItemTransform
            {
                ItemId = 0x11223344,
                OwnerPlayerId = 2,
                Sequence = 41999,
                Flags = ItemTransform.FlagFinal,
                Position = new NetVector3(-12.5f, 0.8f, 33.25f),
                Rotation = new NetQuaternion(0f, 0.7071f, 0f, 0.7071f),
            };

            var decoded = Assert.IsType<ItemTransform>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ItemId, decoded.ItemId);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.True(decoded.IsFinal);
            Assert.Equal(original.Position.Z, decoded.Position.Z);
            Assert.Equal(original.Rotation.W, decoded.Rotation.W);
        }

        [Fact]
        public void ItemTransform_DefaultFlags_IsNotFinal()
        {
            var decoded = Assert.IsType<ItemTransform>(PacketCodec.Decode(PacketCodec.Encode(new ItemTransform())));
            Assert.False(decoded.IsFinal);
        }

        [Fact]
        public void TimeSync_RoundTrips()
        {
            var original = new TimeSync
            {
                Hour = 17,
                Minutes = 42.5f,
                TempOld = -3.25f,
                TempNew = -11f,
                Snowing = true,
                ForecastIndex = 4,
            };

            var decoded = Assert.IsType<TimeSync>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Hour, decoded.Hour);
            Assert.Equal(original.Minutes, decoded.Minutes);
            Assert.Equal(original.TempOld, decoded.TempOld);
            Assert.Equal(original.TempNew, decoded.TempNew);
            Assert.True(decoded.Snowing);
            Assert.Equal(original.ForecastIndex, decoded.ForecastIndex);
        }

        [Fact]
        public void WorldSnapshotRequest_RoundTrips()
        {
            var original = new WorldSnapshotRequest { IdHash = 0x9882BD01 };
            var decoded = Assert.IsType<WorldSnapshotRequest>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.IdHash, decoded.IdHash);
        }

        [Fact]
        public void WorldDoorSnapshot_RoundTrips()
        {
            var original = new WorldDoorSnapshot();
            original.Entries.Add(new WorldDoorSnapshot.Entry { NetId = 1, StateName = "Open door" });
            original.Entries.Add(new WorldDoorSnapshot.Entry { NetId = 0xFFFFFFFF, StateName = "Close" });

            var decoded = Assert.IsType<WorldDoorSnapshot>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(2, decoded.Entries.Count);
            Assert.Equal(original.Entries[0].NetId, decoded.Entries[0].NetId);
            Assert.Equal(original.Entries[0].StateName, decoded.Entries[0].StateName);
            Assert.Equal(original.Entries[1].NetId, decoded.Entries[1].NetId);
            Assert.Equal(original.Entries[1].StateName, decoded.Entries[1].StateName);
        }

        [Fact]
        public void WorldDoorSnapshot_Empty_RoundTrips()
        {
            var decoded = Assert.IsType<WorldDoorSnapshot>(PacketCodec.Decode(PacketCodec.Encode(new WorldDoorSnapshot())));
            Assert.Empty(decoded.Entries);
        }

        [Fact]
        public void PassengerState_RoundTrips()
        {
            var original = new PassengerState { PlayerId = 3, VehicleId = 0xCAFEBABE, SeatIndex = 2 };
            var decoded = Assert.IsType<PassengerState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.VehicleId, decoded.VehicleId);
            Assert.Equal(original.SeatIndex, decoded.SeatIndex);
            Assert.True(decoded.IsSeated);
        }

        [Fact]
        public void PassengerState_Default_IsNotSeated()
        {
            var decoded = Assert.IsType<PassengerState>(PacketCodec.Decode(PacketCodec.Encode(new PassengerState())));
            Assert.False(decoded.IsSeated);
            Assert.Equal(PassengerState.SeatNone, decoded.SeatIndex);
        }

        [Fact]
        public void WorldItemSnapshot_RoundTrips()
        {
            var original = new WorldItemSnapshot();
            original.Entries.Add(new WorldItemSnapshot.Entry
            {
                ItemId = 0xABCD1234,
                Position = new NetVector3(101.5f, -2.25f, 998f),
                Rotation = new NetQuaternion(0f, 0f, 0.7071f, 0.7071f),
            });

            var decoded = Assert.IsType<WorldItemSnapshot>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Single(decoded.Entries);
            Assert.Equal(original.Entries[0].ItemId, decoded.Entries[0].ItemId);
            Assert.Equal(original.Entries[0].Position.X, decoded.Entries[0].Position.X);
            Assert.Equal(original.Entries[0].Rotation.Z, decoded.Entries[0].Rotation.Z);
        }
    }
}
