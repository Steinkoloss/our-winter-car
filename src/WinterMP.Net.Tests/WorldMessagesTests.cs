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
        public void VehicleState_RoundTrips()
        {
            var original = new VehicleState
            {
                VehicleId = 0x44556677,
                OwnerPlayerId = 1,
                Sequence = 1234,
                Flags = VehicleState.FlagEngineOn | VehicleState.FlagAccOn
                    | VehicleState.FlagBlinkerLeft | VehicleState.FlagHazard,
                Rpm = 3450,
                SpeedTenthsKmh = 452,
                FuelLevel = 192,
                CoolantTemp = 128,
            };

            var decoded = Assert.IsType<VehicleState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.VehicleId, decoded.VehicleId);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.True(decoded.EngineOn);
            Assert.True(decoded.AccOn);
            Assert.True(decoded.BlinkerLeft);
            Assert.False(decoded.BlinkerRight);
            Assert.True(decoded.HazardOn);
            Assert.Equal(original.Rpm, decoded.Rpm);
            Assert.Equal(original.SpeedTenthsKmh, decoded.SpeedTenthsKmh);
            Assert.Equal(original.FuelLevel, decoded.FuelLevel);
            Assert.Equal(original.CoolantTemp, decoded.CoolantTemp);
        }

        [Fact]
        public void VehicleState_Default_EngineOff()
        {
            var decoded = Assert.IsType<VehicleState>(PacketCodec.Decode(PacketCodec.Encode(new VehicleState())));
            Assert.False(decoded.EngineOn);
        }

        [Fact]
        public void VehicleClimate_RoundTrips()
        {
            var original = new VehicleClimate
            {
                VehicleId = 0xAABBCCDD,
                OwnerPlayerId = 2,
                Sequence = 9001,
                Frost = 200,
                Flags = VehicleClimate.FlagWindowHeater | VehicleClimate.FlagGlassDefrosting
                    | VehicleClimate.FlagPlayerIn,
                HeaterTemp = 180,
                HeaterBlower = 64,
                HeaterDirection = 128,
                Fog = 150,
                CabinTemp = 96,
            };

            var decoded = Assert.IsType<VehicleClimate>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.VehicleId, decoded.VehicleId);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Frost, decoded.Frost);
            Assert.True(decoded.WindowHeaterOn);
            Assert.True(decoded.GlassDefrosting);
            Assert.True(decoded.PlayerIn);
            Assert.Equal(original.HeaterTemp, decoded.HeaterTemp);
            Assert.Equal(original.HeaterBlower, decoded.HeaterBlower);
            Assert.Equal(original.HeaterDirection, decoded.HeaterDirection);
            Assert.Equal(original.Fog, decoded.Fog);
            Assert.Equal(original.CabinTemp, decoded.CabinTemp);
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
                DaysPassed = 42,
                DayOfWeek = 3,
            };

            var decoded = Assert.IsType<TimeSync>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Hour, decoded.Hour);
            Assert.Equal(original.Minutes, decoded.Minutes);
            Assert.Equal(original.TempOld, decoded.TempOld);
            Assert.Equal(original.TempNew, decoded.TempNew);
            Assert.True(decoded.Snowing);
            Assert.Equal(original.ForecastIndex, decoded.ForecastIndex);
            Assert.Equal(original.DaysPassed, decoded.DaysPassed);
            Assert.Equal(original.DayOfWeek, decoded.DayOfWeek);
        }

        [Fact]
        public void WalletState_RoundTrips()
        {
            var original = new WalletState { Money = 1234.5f, Sequence = 99 };
            var decoded = Assert.IsType<WalletState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Money, decoded.Money);
            Assert.Equal(original.Sequence, decoded.Sequence);
        }

        [Fact]
        public void PurchaseIntent_RoundTrips()
        {
            var original = new PurchaseIntent
            {
                PlayerId = 2,
                NetId = 0xCAFEBABE,
                EventName = "USE",
                Sequence = 7,
            };

            var decoded = Assert.IsType<PurchaseIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.NetId, decoded.NetId);
            Assert.Equal(original.EventName, decoded.EventName);
            Assert.Equal(original.Sequence, decoded.Sequence);
        }

        [Fact]
        public void ItemDespawn_RoundTrips()
        {
            var original = new ItemDespawn { ItemId = 0xAABBCCDD };
            var decoded = Assert.IsType<ItemDespawn>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ItemId, decoded.ItemId);
        }

        [Fact]
        public void BoltState_RoundTrips()
        {
            var original = new BoltState
            {
                NetId = 0x12345678,
                BoltTightness = 7,
                ScrewInt = 14,
            };

            var decoded = Assert.IsType<BoltState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NetId, decoded.NetId);
            Assert.Equal(original.BoltTightness, decoded.BoltTightness);
            Assert.Equal(original.ScrewInt, decoded.ScrewInt);
        }

        [Fact]
        public void PartState_RoundTrips()
        {
            var original = new PartState
            {
                NetId = 0xDEADBEEF,
                Flags = PartState.FlagInstalled,
                Tightness = 200,
                Wear = 42,
            };

            var decoded = Assert.IsType<PartState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NetId, decoded.NetId);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.Tightness, decoded.Tightness);
            Assert.Equal(original.Wear, decoded.Wear);
        }

        [Fact]
        public void WorldSnapshotRequest_RoundTrips()
        {
            var original = new WorldSnapshotRequest { IdHash = 0x9882BD01 };
            var decoded = Assert.IsType<WorldSnapshotRequest>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.IdHash, decoded.IdHash);
        }

        [Fact]
        public void WorldStateChecksum_RoundTrips()
        {
            var original = new WorldStateChecksum
            {
                WalletCrc = 0x12345678,
                WorldCrc = 0xABCDEF01,
                ItemCrc = 0x55AA55AA,
                VehicleCrc = 0xDEADBEEF,
                Sequence = 42,
            };
            var decoded = Assert.IsType<WorldStateChecksum>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.WalletCrc, decoded.WalletCrc);
            Assert.Equal(original.WorldCrc, decoded.WorldCrc);
            Assert.Equal(original.ItemCrc, decoded.ItemCrc);
            Assert.Equal(original.VehicleCrc, decoded.VehicleCrc);
            Assert.Equal(original.Sequence, decoded.Sequence);
        }

        [Fact]
        public void WorldObjectStateRequest_RoundTrips()
        {
            var original = new WorldObjectStateRequest { NetId = 0xCAFEBABE };
            var decoded = Assert.IsType<WorldObjectStateRequest>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NetId, decoded.NetId);
        }

        [Fact]
        public void WorldResyncRequest_RoundTrips()
        {
            var original = new WorldResyncRequest
            {
                Flags = WorldResyncRequest.FlagWallet | WorldResyncRequest.FlagParts,
                ChecksumSequence = 7,
            };
            var decoded = Assert.IsType<WorldResyncRequest>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.ChecksumSequence, decoded.ChecksumSequence);
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
        public void GuestSpawn_RoundTrips()
        {
            var original = new GuestSpawn
            {
                HostPosition = new NetVector3(1f, 2f, 3f),
                HostRotation = NetQuaternion.Identity,
                LastPosition = new NetVector3(10f, 0.1f, -8f),
                LastRotation = new NetQuaternion(0f, 0.707f, 0f, 0.707f),
                Flags = GuestSpawn.FlagHasLastPosition | GuestSpawn.FlagHasSavedNeeds,
                Hunger = 12.5f,
                Fatigue = 88f,
                Thirst = 40f,
                Urine = 5f,
            };
            var decoded = Assert.IsType<GuestSpawn>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.HostPosition.X, decoded.HostPosition.X, 3);
            Assert.Equal(original.LastPosition.Z, decoded.LastPosition.Z, 3);
            Assert.True(decoded.HasLastPosition);
            Assert.True(decoded.HasSavedNeeds);
            Assert.Equal(original.Hunger, decoded.Hunger, 3);
            Assert.Equal(original.Urine, decoded.Urine, 3);
        }

        [Fact]
        public void PlayerNeedsReport_RoundTrips()
        {
            var original = new PlayerNeedsReport
            {
                PlayerId = 2,
                Hunger = 10f,
                Fatigue = 20f,
                Thirst = 30f,
                Urine = 40f,
                Sequence = 7,
            };
            var decoded = Assert.IsType<PlayerNeedsReport>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Thirst, decoded.Thirst, 3);
        }

        [Fact]
        public void SleepConsent_RoundTrips()
        {
            var request = new SleepConsentRequest { RequestId = 3, InitiatorPlayerId = 0 };
            var decodedRequest = Assert.IsType<SleepConsentRequest>(
                PacketCodec.Decode(PacketCodec.Encode(request)));
            Assert.Equal(request.RequestId, decodedRequest.RequestId);

            var response = new SleepConsentResponse { RequestId = 3, PlayerId = 2, Accepted = true };
            var decodedResponse = Assert.IsType<SleepConsentResponse>(
                PacketCodec.Decode(PacketCodec.Encode(response)));
            Assert.True(decodedResponse.Accepted);
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

        [Fact]
        public void WorldBoltSnapshot_RoundTrips()
        {
            var original = new WorldBoltSnapshot();
            original.Entries.Add(new WorldBoltSnapshot.Entry
            {
                NetId = 0xAABBCCDD,
                BoltTightness = 5,
                ScrewInt = 10,
            });

            var decoded = Assert.IsType<WorldBoltSnapshot>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Single(decoded.Entries);
            Assert.Equal(original.Entries[0].NetId, decoded.Entries[0].NetId);
            Assert.Equal(original.Entries[0].BoltTightness, decoded.Entries[0].BoltTightness);
            Assert.Equal(original.Entries[0].ScrewInt, decoded.Entries[0].ScrewInt);
        }

        [Fact]
        public void WorldItemDespawnSnapshot_RoundTrips()
        {
            var original = new WorldItemDespawnSnapshot();
            original.ItemIds.Add(0x11223344);
            original.ItemIds.Add(0xAABBCCDD);

            var decoded = Assert.IsType<WorldItemDespawnSnapshot>(
                PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ItemIds, decoded.ItemIds);
        }

        [Fact]
        public void WorldPartSnapshot_RoundTrips()
        {
            var original = new WorldPartSnapshot();
            original.Entries.Add(new WorldPartSnapshot.Entry
            {
                NetId = 0x11223344,
                Flags = PartState.FlagInstalled,
                Tightness = 128,
                Wear = 10,
            });

            var decoded = Assert.IsType<WorldPartSnapshot>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Single(decoded.Entries);
            Assert.Equal(original.Entries[0].NetId, decoded.Entries[0].NetId);
            Assert.Equal(original.Entries[0].Flags, decoded.Entries[0].Flags);
            Assert.Equal(original.Entries[0].Tightness, decoded.Entries[0].Tightness);
            Assert.Equal(original.Entries[0].Wear, decoded.Entries[0].Wear);
        }
    }
}
