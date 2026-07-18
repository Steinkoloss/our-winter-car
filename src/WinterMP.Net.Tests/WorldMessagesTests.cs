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
            Assert.False(decoded.HasVelocity);
        }

        [Fact]
        public void ItemTransform_VelocityRoundTripsOnlyWhenFlagged()
        {
            var withVelocity = new ItemTransform
            {
                ItemId = 7,
                OwnerPlayerId = 1,
                Sequence = 99,
                Flags = ItemTransform.FlagVehicle | ItemTransform.FlagDriver | ItemTransform.FlagHasVelocity,
                Position = new NetVector3(1f, 2f, 3f),
                Rotation = NetQuaternion.Identity,
                Velocity = new NetVector3(-8.25f, 0.5f, 19.75f),
            };

            var decoded = Assert.IsType<ItemTransform>(PacketCodec.Decode(PacketCodec.Encode(withVelocity)));
            Assert.True(decoded.HasVelocity);
            Assert.Equal(withVelocity.Velocity.X, decoded.Velocity.X);
            Assert.Equal(withVelocity.Velocity.Z, decoded.Velocity.Z);

            // Without the flag the field stays off the wire entirely.
            var withoutVelocity = new ItemTransform
            {
                ItemId = 7,
                Flags = ItemTransform.FlagVehicle,
                Velocity = new NetVector3(1f, 2f, 3f),
            };

            var bare = Assert.IsType<ItemTransform>(PacketCodec.Decode(PacketCodec.Encode(withoutVelocity)));
            Assert.False(bare.HasVelocity);
            Assert.Equal(0f, bare.Velocity.X);
        }

        [Fact]
        public void VehicleCargo_RoundTrips()
        {
            var original = new VehicleCargo
            {
                VehicleId = 0xCAFE1234,
                OwnerPlayerId = 3,
                Sequence = 777,
                Entries = new[]
                {
                    new VehicleCargo.Entry
                    {
                        ItemId = 0x11,
                        LocalPosition = new NetVector3(0.5f, -0.25f, 1.75f),
                        LocalRotation = new NetQuaternion(0f, 0.7071f, 0f, 0.7071f),
                    },
                    new VehicleCargo.Entry
                    {
                        ItemId = 0x22,
                        LocalPosition = new NetVector3(-1f, 0f, 2f),
                        LocalRotation = NetQuaternion.Identity,
                    },
                },
            };

            var decoded = Assert.IsType<VehicleCargo>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.VehicleId, decoded.VehicleId);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(2, decoded.Entries.Length);
            Assert.Equal(0x11u, decoded.Entries[0].ItemId);
            Assert.Equal(0.5f, decoded.Entries[0].LocalPosition.X);
            Assert.Equal(0.7071f, decoded.Entries[0].LocalRotation.Y);
            Assert.Equal(0x22u, decoded.Entries[1].ItemId);
            Assert.Equal(2f, decoded.Entries[1].LocalPosition.Z);
        }

        [Fact]
        public void VehicleCargo_EmptySetRoundTrips()
        {
            var original = new VehicleCargo { VehicleId = 42, OwnerPlayerId = 1, Sequence = 5 };
            var decoded = Assert.IsType<VehicleCargo>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(42u, decoded.VehicleId);
            Assert.Empty(decoded.Entries);
        }

        [Fact]
        public void VehicleCargo_WriteCapsEntriesAtMax()
        {
            var entries = new VehicleCargo.Entry[VehicleCargo.MaxEntries + 8];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new VehicleCargo.Entry { ItemId = (uint)i, LocalRotation = NetQuaternion.Identity };

            var decoded = Assert.IsType<VehicleCargo>(PacketCodec.Decode(PacketCodec.Encode(
                new VehicleCargo { VehicleId = 1, Entries = entries })));
            Assert.Equal(VehicleCargo.MaxEntries, decoded.Entries.Length);
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
                Ice = 233,
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
            Assert.Equal(original.Ice, decoded.Ice);
        }

        [Fact]
        public void VehicleState_SnapshotSequence_RoundTrips()
        {
            // The join/resync snapshot builders tag VehicleState with SnapshotSequence so
            // the receiver applies it without the live-stream dedup. Lock the wire value
            // (shared Net<->Core contract) and that it survives a round-trip.
            Assert.Equal(ushort.MaxValue, VehicleState.SnapshotSequence);

            var original = new VehicleState { VehicleId = 7, Sequence = VehicleState.SnapshotSequence };
            var decoded = Assert.IsType<VehicleState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(VehicleState.SnapshotSequence, decoded.Sequence);
        }

        [Fact]
        public void VehicleClimate_SnapshotSequence_RoundTrips()
        {
            Assert.Equal(ushort.MaxValue, VehicleClimate.SnapshotSequence);

            var original = new VehicleClimate { VehicleId = 7, Sequence = VehicleClimate.SnapshotSequence };
            var decoded = Assert.IsType<VehicleClimate>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(VehicleClimate.SnapshotSequence, decoded.Sequence);
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
        public void ItemSpawn_RoundTrips()
        {
            var original = new ItemSpawn
            {
                ContainerNetId = 0x0BADF00D,
                Epoch = 42,
                OwnerPlayerId = 1,
                StateName = "Spawn all",
                Flags = ItemSpawn.FlagReplay,
                OfferSequence = 17,
            };
            original.Items.Add(new ItemSpawn.Entry
            {
                NetId = 0x11112222,
                TemplateName = "potato chips(itemx)",
                Position = new NetVector3(1.5f, 2.25f, -3.75f),
                Rotation = new NetQuaternion(0f, 0.7071f, 0f, 0.7071f),
            });
            original.Items.Add(new ItemSpawn.Entry
            {
                NetId = 0x33334444,
                TemplateName = "sausages(itemx)",
                Position = new NetVector3(-9f, 0.1f, 4f),
                Rotation = NetQuaternion.Identity,
            });

            var decoded = Assert.IsType<ItemSpawn>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ContainerNetId, decoded.ContainerNetId);
            Assert.Equal(original.Epoch, decoded.Epoch);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.StateName, decoded.StateName);
            Assert.Equal(original.Items.Count, decoded.Items.Count);
            Assert.Equal(original.Items[0].NetId, decoded.Items[0].NetId);
            Assert.Equal(original.Items[0].TemplateName, decoded.Items[0].TemplateName);
            Assert.Equal(original.Items[0].Position.Z, decoded.Items[0].Position.Z);
            Assert.Equal(original.Items[1].NetId, decoded.Items[1].NetId);
            Assert.Equal(original.Items[1].TemplateName, decoded.Items[1].TemplateName);
            Assert.Equal(original.Items[1].Rotation.W, decoded.Items[1].Rotation.W);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.True(decoded.IsReplay);
            Assert.Equal(original.OfferSequence, decoded.OfferSequence);
        }

        [Fact]
        public void ItemSpawn_EmptyManifest_RoundTrips()
        {
            var original = new ItemSpawn { ContainerNetId = 7, Epoch = 1, StateName = "Spawn one" };
            var decoded = Assert.IsType<ItemSpawn>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ContainerNetId, decoded.ContainerNetId);
            Assert.Empty(decoded.Items);
        }

        [Fact]
        public void SpawnIntent_RoundTrips()
        {
            var original = new SpawnIntent
            {
                PlayerId = 3,
                ContainerNetId = 0x0BADF00D,
                StateName = "Spawn all",
                Sequence = 9,
            };
            original.Items.Add(new SpawnIntent.Entry
            {
                TemplateName = "sausages(itemx)",
                Position = new NetVector3(1.5f, 0.25f, -3f),
                Rotation = new NetQuaternion(0f, 0.7071f, 0f, 0.7071f),
            });
            original.Items.Add(new SpawnIntent.Entry
            {
                TemplateName = "pizza(itemx)",
                Position = new NetVector3(-8f, 12f, 44.5f),
                Rotation = NetQuaternion.Identity,
            });

            var decoded = Assert.IsType<SpawnIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.ContainerNetId, decoded.ContainerNetId);
            Assert.Equal(original.StateName, decoded.StateName);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(2, decoded.Items.Count);
            Assert.Equal(original.Items[0].TemplateName, decoded.Items[0].TemplateName);
            Assert.Equal(original.Items[0].Position.Z, decoded.Items[0].Position.Z);
            Assert.Equal(original.Items[1].TemplateName, decoded.Items[1].TemplateName);
            Assert.Equal(original.Items[1].Rotation.W, decoded.Items[1].Rotation.W);
        }

        [Fact]
        public void SpawnIntent_EmptyItems_RoundTrips()
        {
            var original = new SpawnIntent { PlayerId = 1, ContainerNetId = 42, StateName = "Spawn one", Sequence = 2 };
            var decoded = Assert.IsType<SpawnIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Empty(decoded.Items);
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
                BodyTemp = 36.6f,
                Stress = 42f,
                Drunk = 3.7f,
            };
            var decoded = Assert.IsType<GuestSpawn>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.HostPosition.X, decoded.HostPosition.X, 3);
            Assert.Equal(original.LastPosition.Z, decoded.LastPosition.Z, 3);
            Assert.True(decoded.HasLastPosition);
            Assert.True(decoded.HasSavedNeeds);
            Assert.Equal(original.Hunger, decoded.Hunger, 3);
            Assert.Equal(original.Urine, decoded.Urine, 3);
            Assert.Equal(original.BodyTemp, decoded.BodyTemp, 3);
            Assert.Equal(original.Stress, decoded.Stress, 3);
            Assert.Equal(original.Drunk, decoded.Drunk, 3);
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
                BodyTemp = 50f,
                Stress = 60f,
                Drunk = 4.2f,
                Sequence = 7,
            };
            var decoded = Assert.IsType<PlayerNeedsReport>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Thirst, decoded.Thirst, 3);
            Assert.Equal(original.BodyTemp, decoded.BodyTemp, 3);
            Assert.Equal(original.Stress, decoded.Stress, 3);
            Assert.Equal(original.Drunk, decoded.Drunk, 3);
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

            var result = new SleepConsentResult { RequestId = 3, Accepted = false };
            var decodedResult = Assert.IsType<SleepConsentResult>(
                PacketCodec.Decode(PacketCodec.Encode(result)));
            Assert.Equal(result.RequestId, decodedResult.RequestId);
            Assert.False(decodedResult.Accepted);
        }

        [Fact]
        public void PlayerDeath_RoundTrips()
        {
            var report = new PlayerDeathReport { PlayerId = 2, Cause = DeathCause.Fatigue, Sequence = 1 };
            var decodedReport = Assert.IsType<PlayerDeathReport>(PacketCodec.Decode(PacketCodec.Encode(report)));
            Assert.Equal(DeathCause.Fatigue, decodedReport.Cause);

            var evt = new PlayerDeathEvent
            {
                PlayerId = 2,
                Cause = DeathCause.Drown,
                Flags = PlayerDeathEventFlags.PermadeathWipe,
            };
            var decodedEvt = Assert.IsType<PlayerDeathEvent>(PacketCodec.Decode(PacketCodec.Encode(evt)));
            Assert.True((decodedEvt.Flags & PlayerDeathEventFlags.PermadeathWipe) != 0);

            var respawn = new PlayerRespawn
            {
                PlayerId = 2,
                Position = new NetVector3(1f, 2f, 3f),
                Rotation = NetQuaternion.Identity,
                Sequence = 4,
            };
            var decodedRespawn = Assert.IsType<PlayerRespawn>(PacketCodec.Decode(PacketCodec.Encode(respawn)));
            Assert.Equal(respawn.Position.Y, decodedRespawn.Position.Y, 3);
        }

        [Fact]
        public void DeathCause_ValuesAreWireStable()
        {
            // Cause bytes are append-only wire contract — reordering or reuse would make
            // peers display the wrong death. Pin every value.
            Assert.Equal(0, DeathCause.Unknown);
            Assert.Equal(1, DeathCause.Fatigue);
            Assert.Equal(2, DeathCause.Hunger);
            Assert.Equal(3, DeathCause.Thirst);
            Assert.Equal(4, DeathCause.Urine);
            Assert.Equal(5, DeathCause.Stress);
            Assert.Equal(6, DeathCause.RunOver);
            Assert.Equal(7, DeathCause.Drown);
            Assert.Equal(8, DeathCause.Fire);
            Assert.Equal(9, DeathCause.Electrocute);
            Assert.Equal(10, DeathCause.Hypothermia);
            Assert.Equal(11, DeathCause.Murder);
            Assert.Equal(12, DeathCause.Train);
            Assert.Equal(13, DeathCause.Accident);
            Assert.Equal(14, DeathCause.Sewage);
            Assert.Equal(15, DeathCause.Carbon);
            Assert.Equal(16, DeathCause.Pto);
            Assert.Equal(17, DeathCause.CutterBlade);
            Assert.Equal(18, DeathCause.InJail);
            Assert.Equal(19, DeathCause.PissTv);
            Assert.Equal(20, DeathCause.Burn);
            Assert.Equal(21, DeathCause.Smoking);

            var report = new PlayerDeathReport { PlayerId = 5, Cause = DeathCause.Carbon, Sequence = 2 };
            var decoded = Assert.IsType<PlayerDeathReport>(PacketCodec.Decode(PacketCodec.Encode(report)));
            Assert.Equal(DeathCause.Carbon, decoded.Cause);
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

        [Fact]
        public void NpcTransform_RoundTrips()
        {
            var original = new NpcTransform
            {
                NetId = 0xAABBCCDD,
                Sequence = 9001,
                Flags = NpcTransform.FlagFinal,
                Position = new NetVector3(100f, 1.5f, -40f),
                Rotation = NetQuaternion.Identity,
            };

            var decoded = Assert.IsType<NpcTransform>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.NetId, decoded.NetId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.True(decoded.IsFinal);
            Assert.Equal(original.Position.X, decoded.Position.X);
        }
    }
}
