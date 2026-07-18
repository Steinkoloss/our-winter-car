using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class ProgressMessagesTests
    {
        [Fact]
        public void FluidContainerState_RoundTrips()
        {
            var original = new FluidContainerState
            {
                ItemId = 0xCAFE1234,
                OwnerPlayerId = 2,
                Sequence = 99,
                Flags = FluidContainerState.FlagPouring,
                Level = 7.25f,
                Capacity = 10f,
            };

            var decoded = Assert.IsType<FluidContainerState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ItemId, decoded.ItemId);
            Assert.Equal(original.OwnerPlayerId, decoded.OwnerPlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.True(decoded.IsPouring);
            Assert.Equal(original.Level, decoded.Level);
            Assert.Equal(original.Capacity, decoded.Capacity);
        }

        [Fact]
        public void WorldProgressState_RoundTrips()
        {
            var original = new WorldProgressState
            {
                Kind = WorldProgressKind.Classifieds,
                Phase = 3,
                Sequence = 421,
                Primary = 12,
                Secondary = 4,
                Tertiary = -1,
                Value = 345.5f,
            };

            var decoded = Assert.IsType<WorldProgressState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.Equal(original.Phase, decoded.Phase);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Primary, decoded.Primary);
            Assert.Equal(original.Secondary, decoded.Secondary);
            Assert.Equal(original.Tertiary, decoded.Tertiary);
            Assert.Equal(original.Value, decoded.Value);
        }

        [Fact]
        public void JobSiteState_RoundTrips()
        {
            var original = new JobSiteState
            {
                SiteId = 0x4411AA22,
                Kind = JobSiteState.KindSewage,
                Flags = JobSiteState.FlagActive,
                Sequence = 65530,
                Primary = 1843.25f,
                Secondary = 42.5f,
            };

            var decoded = Assert.IsType<JobSiteState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SiteId, decoded.SiteId);
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.True(decoded.IsActive);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Primary, decoded.Primary);
            Assert.Equal(original.Secondary, decoded.Secondary);
        }

        [Fact]
        public void JobSiteState_SewageTruckFlags_RoundTrip()
        {
            var original = new JobSiteState
            {
                SiteId = 0xF1A75EED,
                Kind = JobSiteState.KindSewageTruck,
                Flags = JobSiteState.FlagActive
                    | JobSiteState.FlagHoseAttached
                    | JobSiteState.FlagHoseInWaste
                    | JobSiteState.FlagSucking,
                Sequence = 42,
                Primary = 5000f,
                Secondary = 0.75f,
            };

            var decoded = Assert.IsType<JobSiteState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.True(decoded.IsActive);
            Assert.Equal(original.Primary, decoded.Primary);
            Assert.Equal(original.Secondary, decoded.Secondary);
        }

        [Fact]
        public void MailOrderState_RoundTripsSavedPackageData()
        {
            var original = new MailOrderState
            {
                Kind = MailOrderState.KindAmis,
                Flags = MailOrderState.FlagActive | MailOrderState.FlagSavePosition,
                Sequence = 113,
                Price = 1249.5f,
                WaitTime = 2.5f,
                PriceInt = 1249,
                Data1 = "order-id",
                Data2 = "package-type",
                Data3 = "spawn-slot",
            };

            var decoded = Assert.IsType<MailOrderState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.True(decoded.IsActive);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Price, decoded.Price);
            Assert.Equal(original.WaitTime, decoded.WaitTime);
            Assert.Equal(original.PriceInt, decoded.PriceInt);
            Assert.Equal(original.Data1, decoded.Data1);
            Assert.Equal(original.Data2, decoded.Data2);
            Assert.Equal(original.Data3, decoded.Data3);
        }

        [Fact]
        public void MailOrderIntent_RoundTripsSelectedPackageData()
        {
            var original = new MailOrderIntent
            {
                OrderNetId = 0xA11CE001,
                PlayerId = 2,
                Kind = MailOrderState.KindYellowPages,
                Flags = MailOrderState.FlagActive,
                Sequence = 909,
                Price = 799.25f,
                WaitTime = 1.5f,
                PriceInt = 799,
                Data1 = "listing-id",
                Data2 = "item-id",
                Data3 = "delivery-id",
            };

            var decoded = Assert.IsType<MailOrderIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.OrderNetId, decoded.OrderNetId);
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Kind, decoded.Kind);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Price, decoded.Price);
            Assert.Equal(original.WaitTime, decoded.WaitTime);
            Assert.Equal(original.PriceInt, decoded.PriceInt);
            Assert.Equal(original.Data1, decoded.Data1);
            Assert.Equal(original.Data2, decoded.Data2);
            Assert.Equal(original.Data3, decoded.Data3);
        }

        [Fact]
        public void InspectionState_RoundTripsChecklistAndRenewal()
        {
            var original = new InspectionState
            {
                Flags = InspectionState.FlagCarInspected
                    | InspectionState.FlagStampOnPaper
                    | InspectionState.FlagPassed
                    | InspectionState.FlagStampIssued,
                Sequence = 65200,
                NextInspectionDay = 147,
                InspectionIntervalDays = 365,
                InspectionIntervalLetter = 30,
                ChecklistLow = 0xCAFE1234,
                ChecklistHigh = 0x0000003F,
                PlateFlags = 0x0F,
                StandardPlate = "ABC-123",
                MuseumPlate = "MUS-001",
            };

            var decoded = Assert.IsType<InspectionState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.True(decoded.Passed);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.NextInspectionDay, decoded.NextInspectionDay);
            Assert.Equal(original.InspectionIntervalDays, decoded.InspectionIntervalDays);
            Assert.Equal(original.InspectionIntervalLetter, decoded.InspectionIntervalLetter);
            Assert.Equal(original.ChecklistLow, decoded.ChecklistLow);
            Assert.Equal(original.ChecklistHigh, decoded.ChecklistHigh);
            Assert.Equal(original.PlateFlags, decoded.PlateFlags);
            Assert.Equal(original.StandardPlate, decoded.StandardPlate);
            Assert.Equal(original.MuseumPlate, decoded.MuseumPlate);
        }

        [Fact]
        public void VehicleFuelIntent_RoundTrips()
        {
            var original = new VehicleFuelIntent
            {
                VehicleId = 0xAABBCCDD,
                NozzleNetId = 0x11223344,
                PlayerId = 7,
                Sequence = 60001,
                TargetFuelLevel = 201,
            };

            var decoded = Assert.IsType<VehicleFuelIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.VehicleId, decoded.VehicleId);
            Assert.Equal(original.NozzleNetId, decoded.NozzleNetId);
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.TargetFuelLevel, decoded.TargetFuelLevel);
        }

        [Fact]
        public void PoliceIntent_RoundTrips()
        {
            var original = new PoliceIntent
            {
                PlayerId = 4,
                OffenceFlags = 0x89,
                Sequence = 60001,
                CheckpointId = 0xC0FFEE12,
                Fine = 740.5f,
            };

            var decoded = Assert.IsType<PoliceIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.OffenceFlags, decoded.OffenceFlags);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.CheckpointId, decoded.CheckpointId);
            Assert.Equal(original.Fine, decoded.Fine);
        }

        [Fact]
        public void PoliceState_RoundTrips()
        {
            var original = new PoliceState
            {
                PlayerId = 4,
                OffenceFlags = 0x89,
                Flags = PoliceState.FlagActive,
                Sequence = 60002,
                CheckpointId = 0xC0FFEE12,
                Fine = 740.5f,
            };

            var decoded = Assert.IsType<PoliceState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.OffenceFlags, decoded.OffenceFlags);
            Assert.True(decoded.IsActive);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.CheckpointId, decoded.CheckpointId);
            Assert.Equal(original.Fine, decoded.Fine);
        }

        [Fact]
        public void HomeStereoState_RoundTrips()
        {
            var original = new HomeStereoState
            {
                Flags = HomeStereoState.FlagRadioOn | HomeStereoState.FlagChannel,
                Sequence = 4422,
                Volume = 0.75f,
                Bass = -0.25f,
            };

            var decoded = Assert.IsType<HomeStereoState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.True(decoded.RadioOn);
            Assert.True(decoded.Channel);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Volume, decoded.Volume);
            Assert.Equal(original.Bass, decoded.Bass);
        }

        [Fact]
        public void HomeStereoIntent_RoundTrips()
        {
            var original = new HomeStereoIntent
            {
                PlayerId = 3,
                Flags = HomeStereoState.FlagRadioOn,
                Sequence = 4423,
                Volume = 0.25f,
                Bass = 0.5f,
            };

            var decoded = Assert.IsType<HomeStereoIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Volume, decoded.Volume);
            Assert.Equal(original.Bass, decoded.Bass);
        }

        [Fact]
        public void RallyIntent_RoundTrips()
        {
            var original = new RallyIntent
            {
                PlayerId = 2,
                Stage = 3,
                Checkpoint = 4,
                Sequence = 41234,
            };

            var decoded = Assert.IsType<RallyIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Stage, decoded.Stage);
            Assert.Equal(original.Checkpoint, decoded.Checkpoint);
            Assert.Equal(original.Sequence, decoded.Sequence);
        }

        [Fact]
        public void RallyState_RoundTrips()
        {
            var original = new RallyState
            {
                PlayerId = 2,
                Stage = 3,
                Phase = RallyState.PhaseRacing,
                Checkpoint = 4,
                Sequence = 41235,
                ElapsedCentiseconds = 18423,
            };

            var decoded = Assert.IsType<RallyState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Stage, decoded.Stage);
            Assert.Equal(original.Phase, decoded.Phase);
            Assert.Equal(original.Checkpoint, decoded.Checkpoint);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.ElapsedCentiseconds, decoded.ElapsedCentiseconds);
        }

        [Fact]
        public void IceRaceIntent_RoundTrips()
        {
            var original = new IceRaceIntent
            {
                PlayerId = 5,
                Marker = IceRaceIntent.MarkerCheckpoint2,
                Sequence = 50123,
            };

            var decoded = Assert.IsType<IceRaceIntent>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Marker, decoded.Marker);
            Assert.Equal(original.Sequence, decoded.Sequence);
        }

        [Fact]
        public void IceRaceState_RoundTrips()
        {
            var original = new IceRaceState
            {
                PlayerId = 5,
                Mode = IceRaceState.ModeLapRace,
                Checkpoint = 2,
                Laps = 3,
                Sequence = 50124,
                ElapsedCentiseconds = 9821,
            };

            var decoded = Assert.IsType<IceRaceState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.Mode, decoded.Mode);
            Assert.Equal(original.Checkpoint, decoded.Checkpoint);
            Assert.Equal(original.Laps, decoded.Laps);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.ElapsedCentiseconds, decoded.ElapsedCentiseconds);
        }

        [Fact]
        public void IceRaceEventState_RoundTrips()
        {
            var original = new IceRaceEventState
            {
                Flags = IceRaceEventState.FlagGridReady | IceRaceEventState.FlagOnTrack | IceRaceEventState.FlagPlayerRegistered,
                Sequence = 50777,
                CarLimit = 8,
                CarNumber = 3,
                CarsOnTrack = 6,
                HeatStage = 4,
                Lane = 2,
                RaceDistanceFinals = 12,
                RaceDistanceQuals = 6,
                StarterCars = 4,
                Time = 18.75f,
                CarId = "CORRIS",
                Reference = "QUAL2",
                RaceStage = 3,
            };

            var decoded = Assert.IsType<IceRaceEventState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Flags, decoded.Flags);
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.CarLimit, decoded.CarLimit);
            Assert.Equal(original.CarNumber, decoded.CarNumber);
            Assert.Equal(original.CarsOnTrack, decoded.CarsOnTrack);
            Assert.Equal(original.HeatStage, decoded.HeatStage);
            Assert.Equal(original.Lane, decoded.Lane);
            Assert.Equal(original.RaceDistanceFinals, decoded.RaceDistanceFinals);
            Assert.Equal(original.RaceDistanceQuals, decoded.RaceDistanceQuals);
            Assert.Equal(original.StarterCars, decoded.StarterCars);
            Assert.Equal(original.Time, decoded.Time);
            Assert.Equal(original.CarId, decoded.CarId);
            Assert.Equal(original.Reference, decoded.Reference);
            Assert.Equal(original.RaceStage, decoded.RaceStage);
        }

        [Fact]
        public void IceRaceResultsState_RoundTrips()
        {
            var original = new IceRaceResultsState
            {
                Sequence = 50999,
                Names = new[] { "Driver A", "Driver B" },
                Numbers = new[] { "7", "12" },
                Models = new[] { "Corris", "Jokkis" },
                Uas = new[] { "A", "B" },
            };

            var decoded = Assert.IsType<IceRaceResultsState>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Names, decoded.Names);
            Assert.Equal(original.Numbers, decoded.Numbers);
            Assert.Equal(original.Models, decoded.Models);
            Assert.Equal(original.Uas, decoded.Uas);
        }
    }
}
