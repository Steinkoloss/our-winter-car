using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PacketCodecTests
    {
        [Fact]
        public void HandshakeRequest_RoundTrips()
        {
            var original = new HandshakeRequest
            {
                ProtocolVersion = 7,
                ModVersion = "0.1.0",
                GameVersion = "EA v.260102-01",
                CatalogHash = 0xCAFEBABE,
                PlayerName = "Jokke",
            };

            var decoded = Assert.IsType<HandshakeRequest>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.ProtocolVersion, decoded.ProtocolVersion);
            Assert.Equal(original.ModVersion, decoded.ModVersion);
            Assert.Equal(original.GameVersion, decoded.GameVersion);
            Assert.Equal(original.CatalogHash, decoded.CatalogHash);
            Assert.Equal(original.PlayerName, decoded.PlayerName);
        }

        [Fact]
        public void HandshakeResponse_RoundTrips()
        {
            var original = new HandshakeResponse
            {
                Accepted = false,
                Reason = "Mod version mismatch (host 0.2.0, you 0.1.0).",
                PlayerId = 3,
                HostPlayerName = "Host",
                SessionFlags = SessionFlags.PermadeathEnabled,
            };

            var decoded = Assert.IsType<HandshakeResponse>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.False(decoded.Accepted);
            Assert.Equal(original.Reason, decoded.Reason);
            Assert.Equal(original.PlayerId, decoded.PlayerId);
            Assert.Equal(original.HostPlayerName, decoded.HostPlayerName);
            Assert.Equal(SessionFlags.PermadeathEnabled, decoded.SessionFlags & SessionFlags.PermadeathEnabled);
        }

        [Fact]
        public void ChatMessage_RoundTrips()
        {
            var original = new ChatMessage { SenderPlayerId = 2, Text = "perkele" };
            var decoded = Assert.IsType<ChatMessage>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.SenderPlayerId, decoded.SenderPlayerId);
            Assert.Equal(original.Text, decoded.Text);
        }

        [Fact]
        public void PlayerTransform_RoundTrips()
        {
            var original = new PlayerTransform
            {
                PlayerId = 1,
                Sequence = 65000,
                Position = new NetVector3(-1542.7f, 4.25f, 980.1f),
                Rotation = new NetQuaternion(0f, 0.7071f, 0f, 0.7071f),
                MoveState = 4,
            };

            var decoded = Assert.IsType<PlayerTransform>(PacketCodec.Decode(PacketCodec.Encode(original)));
            Assert.Equal(original.Sequence, decoded.Sequence);
            Assert.Equal(original.Position.X, decoded.Position.X);
            Assert.Equal(original.Rotation.W, decoded.Rotation.W);
            Assert.Equal(original.MoveState, decoded.MoveState);
        }

        [Fact]
        public void EveryRegisteredMessage_EncodesAndDecodesWithDefaults()
        {
            foreach (var id in MessageRegistry.KnownIds)
            {
                var message = MessageRegistry.Create(id);
                if (message is MotorOilBottleState mo) { mo.NativeId="motormoil11"; mo.ItemId=WinterMP.Net.Sync.MotorOilPolicy.ItemId(mo.NativeId); mo.Revision=1; mo.Fluid=4; mo.Grade=2; mo.Viscosity=.6f; mo.Rotation=NetQuaternion.Identity; }
            if (message is MotorOilFillerState mf) { mf.Revision=mf.Epoch=1; mf.HeadId=mf.PanId=0; mf.Rotation=359; mf.Oil=2; mf.Contamination=10; mf.Viscosity=.6f; mf.CapRotation=NetQuaternion.Identity; }
            if (message is MotorOilRefillIntent mi) { mi.Epoch=mi.BottleId=1; mi.PlayerId=1; mi.Action=1; }
                if (message is AdvertJobState ad) { ad.Revision = 1; ad.Stage = 2; ad.Sheets = 30; ad.NextDay = 5; ad.Flags = 7; ad.CompletedMask = 3; ad.Scale = 1; ad.Salary = 17; ad.Delivered = 2; ad.Rotation = NetQuaternion.Identity; }
                if (message is AdvertSheetState ads) { ads.ItemId = 1; ads.Rotation = NetQuaternion.Identity; }
                if (message is AdvertPhoneIntent api) { api.Call = 1; api.PlayerId = 1; api.Phone = 0; api.Action = 0; }
            if (message is AdvertPhoneResult apr) { apr.Call = 1; apr.PlayerId = 1; apr.Phone = 0; apr.Status = 0; }
            if (message is AdvertIntent ai) { ai.ItemId = ai.Sequence = ai.ExpectedRevision = 1; ai.Box = 255; ai.PlayerId = 1; }
                if (message is BulbState bulb) { bulb.ItemId = bulb.Revision = 1; bulb.Wear = 95; }
                if (message is TrainState train) train.Sequence = 1;
                if (message is CoffeeIntent ci) { ci.ItemId = ci.Sequence = 1; ci.PlayerId = 1; ci.Action = CoffeeAction.OpenLid; }
                if (message is CoffeeState cs) { cs.ItemId = cs.Revision = 1; }
                if (message is CoffeeDrinkResult cd) { cd.ItemId = cd.Sequence = 1; cd.Amount = .2f; }
                if (message is SausageOpenIntent sausageOpen) { sausageOpen.PlayerId = 1; sausageOpen.PackageId = sausageOpen.SourceId = sausageOpen.Sequence = 1; }
                if (message is SausageState sausage) { sausage.ItemId = sausage.Revision = 1; }
                if (message is TractorTrailerState trailer) trailer.Revision = 1;
                if (message is TractorTrailerMotion trailerMotion) trailerMotion.Revision = trailerMotion.Sequence = 1;
                if (message is TractorTrailerIntent trailerIntent) { trailerIntent.PlayerId = 1; trailerIntent.Revision = trailerIntent.Sequence = 1; }
                if (message is HouseholdFuseState fuses) fuses.Revision = 1;
                if (message is HouseholdFuseIntent fuseIntent) { fuseIntent.Sequence = fuseIntent.ControlRevision = fuseIntent.ItemId = 1; }
                if (message is HouseholdFuseResult fuseResult) fuseResult.Sequence = 1;
                if (message is TaxiFareIntent taxiFare) { taxiFare.Sequence = 1; taxiFare.FareId = 1; }
                if (message is TaxiMeterIntent taxiMeter) taxiMeter.Sequence = 1;
                if (message is TaxiPaydayReadIntent payday) payday.PaydayId = 1;
                if (message is TaxiCallIntent taxiCall) taxiCall.CallId = 1;
                if (message is FirewoodBuyerState buyer) buyer.NetId = 1;
                if (message is UtilityPaymentResult receipt) receipt.Result = UtilityPaymentResult.Unavailable;
                if (message is MooseChopIntent chop) chop.Corpse = 1;
                if (message is MooseCorpseState corpse) corpse.Corpse = 1;
                if (message is MooseMeatState meat) { meat.FactoryId = 1; meat.NativeId = "moosemeat01"; meat.Rotation = NetQuaternion.Identity; }
                if (message is MilkConditionState milk) { milk.NetId = 1; milk.Condition = 87; }
                if (message is CylinderHeadState head) { head.NetId = 1; head.Mass = 12; }
                if (message is ValveAdjustmentState valve) { valve.NetId = 1; valve.Setting = 4; }
                // Coolant frames require a concrete vehicle even when unavailable.
                if (message is VehicleCoolantState coolant) coolant.VehicleId = 1;
                if (message is VehicleWheelHealthState wheelHealth) wheelHealth.VehicleId = 1;
                if (message is WheelPunctureRequest puncture) { puncture.VehicleId = 1; puncture.PlayerId = 1; puncture.Epoch = 1; }
                if (message is VehicleDrivetrainWearState drivetrain) drivetrain.VehicleId = 1;
                if (message is GearboxWearRequest gearboxWear) { gearboxWear.VehicleId = 42; gearboxWear.PlayerId = 1; }
            if (message is GearboxOilUseRequest oilUse) { oilUse.VehicleId = 1; oilUse.PlayerId = 1; oilUse.Phase = 1; }
                if (message is VehicleConditionReleaseAck release)
                { release.Condition.VehicleId = 1; release.Condition.OwnerPlayerId = 1; }
                if (message is StarterWearRequest wear) { wear.VehicleId = 42; wear.PlayerId = 1; wear.Seconds = .25f; }
                if (message is StarterDrawRequest draw)
                { draw.VehicleId = 1; draw.PlayerId = 1; draw.Kind = StarterDrawRequest.Loaded; draw.Count = 1; }
                if (message is StoveKnobIntent stove)
                { stove.ApplianceId = 1; stove.PlayerId = 1; stove.Plate = 0; stove.Direction = 1; }
                if (message is AtfBottleState atfBottle)
                { atfBottle.ItemId = 1; atfBottle.Revision = 1; atfBottle.NativeId = "atfoil01"; atfBottle.Rotation = NetQuaternion.Identity; }
                if (message is AtfFillerState atfFiller)
                { atfFiller.VehicleId = 1; atfFiller.Revision = 1; atfFiller.Rotation = 359; atfFiller.CapLocalRotation = NetQuaternion.Identity; }
                if (message is FleaListingIntent listingIntent) { listingIntent.PlayerId = 1; listingIntent.ItemId = 5; listingIntent.Price = 5; }
                if (message is FleaListingResult listingResult) { listingResult.PlayerId = 1; listingResult.ItemId = 5; listingResult.Result = 0; }
                if (message is FleaSaleState flea) { flea.MoneyTotal = 100; flea.RentDays = 7; flea.Flags = 1; flea.WeekPrice = 150; }
                if (message is FleaSaleIntent fleaIntent) { fleaIntent.PlayerId = 1; fleaIntent.Action = FleaSaleIntent.PayRent; fleaIntent.Weeks = 1; }
                if (message is FleaSaleResult fleaResult) { fleaResult.PlayerId = 1; fleaResult.Action = FleaSaleIntent.PayRent; fleaResult.Result = FleaSaleResult.Accepted; }
            if (message is AtfRefillIntent atfIntent)
                { atfIntent.VehicleId = 1; atfIntent.BottleId = 2; atfIntent.PlayerId = 1; }
                var decoded = PacketCodec.Decode(PacketCodec.Encode(message));
                Assert.Equal(id, decoded.Id);
            }
        }

        [Fact]
        public void UnknownMessageId_ThrowsProtocolException()
        {
            var writer = new NetWriter();
            writer.WriteUInt16(0xFFF0);
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(writer.ToArray()));
        }

        [Fact]
        public void TrailingPayloadBytes_ThrowProtocolException()
        {
            var encoded = PacketCodec.Encode(new WalletState { Money = 42f, Sequence = 7 });
            var padded = new byte[encoded.Length + 1];
            System.Array.Copy(encoded, padded, encoded.Length);
            padded[padded.Length - 1] = 0xA5;

            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(padded));
        }
    }
}
