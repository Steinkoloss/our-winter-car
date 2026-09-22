using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    /// <summary>
    /// Reflection sweep over EVERY registered message: fill every field with a
    /// distinctive non-default value (lists get 2 elements so element ordering is
    /// exercised, nested structs are filled recursively), then assert the wire is
    /// symmetric via the double-encode invariant:
    ///
    ///     Encode(Decode(Encode(m))) == Encode(m)   (byte for byte)
    ///
    /// This catches Write/Read asymmetry — a field written but not read, a field
    /// read but not written, or a reordering across differing types — without
    /// needing a hand-written per-field assertion for all 60+ messages. It is
    /// robust to lossy quantization because an idempotent quantizer re-encodes a
    /// decoded value to the same bytes. Decode also runs RequireEnd internally, so
    /// any trailing/short read throws here.
    /// </summary>
    public class AllMessagesRoundTripTests
    {
        [Fact]
        public void EveryRegisteredMessage_HasSymmetricWireFormat()
        {
            var failures = new List<string>();

            foreach (var id in MessageRegistry.KnownIds)
            {
                IMessage template = MessageRegistry.Create(id);
                try
                {
                    Fill(template, 1);

                    byte[] first = PacketCodec.Encode(template);
                    IMessage decoded = PacketCodec.Decode(first);
                    byte[] second = PacketCodec.Encode(decoded);

                    if (decoded.GetType() != template.GetType())
                        failures.Add($"{id}: decoded as {decoded.GetType().Name}, expected {template.GetType().Name}");
                    else if (!first.SequenceEqual(second))
                        failures.Add($"{id} ({template.GetType().Name}): asymmetric wire — " +
                            $"encode={first.Length}B, re-encode={second.Length}B (Write/Read field mismatch)");
                }
                catch (Exception e)
                {
                    failures.Add($"{id} ({template.GetType().Name}): {e.GetType().Name}: {e.Message}");
                }
            }

            Assert.True(failures.Count == 0,
                "Message wire-format asymmetry detected:\n  " + string.Join("\n  ", failures));
        }

        /// <summary>Recursively assign every public instance field a distinctive non-default value.</summary>
        private static void Fill(object target, int seed)
        {
            foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly) continue;
                var existingArray = field.GetValue(target) as Array;
                field.SetValue(target, MakeValue(field.FieldType, seed,
                    existingArray != null && existingArray.Length > 0 ? existingArray.Length : 2));
                seed += 7;
            }
            if (target is HouseholdFuseState fuses) { fuses.PowerMask = 2047; for (byte i = 0; i < 11; i++) { var h = fuses.Holders[i]; h.Slot = i; h.Fuse = 1; h.Tightness = 8; h.Flags = 3; h.Rotation = NetQuaternion.Identity; } }
            if (target is MotorOilBottleState mo) { mo.NativeId="motormoil11"; mo.ItemId=WinterMP.Net.Sync.MotorOilPolicy.ItemId(mo.NativeId); mo.Revision=1; mo.Fluid=4; mo.Empty=false; mo.Grade=2; mo.Viscosity=.6f; mo.Rotation=NetQuaternion.Identity; }
            if (target is MotorOilFillerState mf) { mf.Revision=mf.Epoch=1; mf.HeadId=mf.PanId=0; mf.Rotation=359; mf.Oil=2; mf.Contamination=10; mf.Viscosity=.6f; mf.CapRotation=NetQuaternion.Identity; }
            if (target is MotorOilRefillIntent mi) { mi.Epoch=mi.BottleId=1; mi.PlayerId=1; mi.Action=1; }
            if (target is AdvertJobState ad) { ad.Revision = 1; ad.Stage = 2; ad.Sheets = 30; ad.NextDay = 5; ad.Flags = 7; ad.CompletedMask = 3; ad.Scale = 1; ad.Salary = 17; ad.Delivered = 2; ad.Rotation = NetQuaternion.Identity; }
            if (target is AdvertSheetState ads) { ads.ItemId = 1; ads.Rotation = NetQuaternion.Identity; }
            if (target is AdvertPhoneIntent api) { api.Call = 1; api.PlayerId = 1; api.Phone = 0; api.Action = 0; }
            if (target is AdvertPhoneResult apr) { apr.Call = 1; apr.PlayerId = 1; apr.Phone = 0; apr.Status = 0; }
            if (target is AdvertIntent ai) { ai.ItemId = ai.Sequence = ai.ExpectedRevision = 1; ai.Box = 255; ai.PlayerId = 1; }
            if (target is BulbState bulb) { bulb.ItemId = bulb.Revision = 1; bulb.Wear = 95; bulb.Rotation = NetQuaternion.Identity; }
            if (target is TrainState train) { train.Phase = 0; train.Flags = 7; train.ColliderMask = 2047; train.Volume = .7f; train.Velocity = new NetVector3(20, 0, 0); train.Rotation = NetQuaternion.Identity; }
            if (target is CoffeeIntent ci) { ci.PlayerId = 1; ci.Action = CoffeeAction.OpenLid; }
            if (target is CoffeeState cs) { cs.Kind = 0; cs.Flags = 1; cs.Water = 1; cs.Ground = 12; cs.Coffee = .4f; cs.Caffeine = .6f; cs.BoilVolume = .3f; cs.Rotation = NetQuaternion.Identity; }
            if (target is CoffeeDrinkResult cd) { cd.PlayerId = 1; cd.Amount = .2f; cd.Caffeine = .7f; }
            if (target is SausageState sausage) { sausage.Kind = 1; sausage.Grilled = true; sausage.Condition = 40; sausage.Rotation = NetQuaternion.Identity; }
            if (target is TractorTrailerState trailer) { trailer.Attached = true; trailer.Owner = 1; trailer.ConnectedAnchor = new NetVector3(0, .4f, -1); }
            if (target is TractorTrailerMotion trailerMotion) trailerMotion.Owner = 1;
            if (target is TrailerBodyPose body) { body.Rotation = NetQuaternion.Identity; body.Position = new NetVector3(1, 2, 3); body.Velocity = new NetVector3(4, 5, 6); body.AngularVelocity = new NetVector3(.1f, .2f, .3f); }
            if (target is HouseholdFuseIntent fuseIntent) { fuseIntent.PlayerId = 1; fuseIntent.Holder = 3; fuseIntent.Slot = 255; fuseIntent.Action = HouseholdFuseAction.InsertFuse; }
            if (target is HouseholdFuseResult fuseResult) { fuseResult.PlayerId = 1; fuseResult.Holder = 3; fuseResult.Accepted = fuseResult.Shock = true; }
            if (target is HouseholdFuseHolder fuseHolder) { fuseHolder.Slot = 255; fuseHolder.Fuse = 1; fuseHolder.Tightness = 0; fuseHolder.Flags = 3; fuseHolder.Rotation = NetQuaternion.Identity; }
            if (target is TaxiFareState taxiFare) { taxiFare.Flags = TaxiFareState.Active | TaxiFareState.Arrived | TaxiFareState.CanCharge; taxiFare.FareId = 1; taxiFare.ReceiptStage = TaxiReceiptStage.Ready; taxiFare.ReceiptFlags = TaxiFareState.PrintVisible; taxiFare.ReceiptRotation = NetQuaternion.Identity; }
            if (target is TaxiMeterState taxiMeter) { taxiMeter.Flags = 511; taxiMeter.Mode = 5; taxiMeter.Values = new float[9]; taxiMeter.KnobRotation = NetQuaternion.Identity; }
            if (target is TaxiServiceState taxiService) { taxiService.PaydayFlags = 3; taxiService.Flags = 255; taxiService.ColliderFlags = 7; taxiService.CustomerRotation = taxiService.WalkerRotation = NetQuaternion.Identity; taxiService.LuggageEpoch = 1; taxiService.LuggageMask = 31; taxiService.LuggagePositions = new NetVector3[5]; taxiService.LuggageRotations = new NetQuaternion[5]; for (int i = 0; i < 5; i++) taxiService.LuggageRotations[i] = NetQuaternion.Identity; }
            if (target is PartFitRequest fitting) { fitting.Operation = PartFitOperation.Install; fitting.SlotIndex = 3; }
            if (target is PartFitReceipt fitted) { fitted.Operation = PartFitOperation.Install; fitted.SlotIndex = 3; }
            if (target is ReplacementPartState replacement) { replacement.ParentKind = PartParentKind.NativePart; replacement.CamProfile = "55003500"; }
            if (target is VehicleCoolantState coolant) { coolant.Flags = 1; coolant.Celsius = -12.375f; coolant.EngineCelsius = 83.625f; }
            if (target is GearboxState gearbox) { gearbox.Flags = 1; gearbox.Type = 2; }
            if (target is VehicleWheelHealthState wheelHealth) { wheelHealth.VehicleId = 42; wheelHealth.Availability = 15; }
            if (target is FirewoodLoadState load) { load.Logs = 400; load.Firewood = 300; load.Mass = 600; load.BedScale = .25f; load.Unloaded = 100; load.Piles = new[] { new FirewoodPile { Position = new NetVector3(1, 2, 3), Scale = .5f } }; }
            if (target is FirewoodUnloadIntent unload) { unload.PlayerId = 1; unload.Epoch = 2; unload.Sequence = 3; unload.Unload = true; }
            if (target is FirewoodBuyerState buyer) { buyer.NetId = 42; buyer.Flags = 3; buyer.Amount = 500; buyer.Position = new NetVector3(13, -3, 141); buyer.Rotation = NetQuaternion.Identity; }
            if (target is MooseChopIntent chop) { chop.PlayerId = 1; chop.Corpse = 5; chop.Section = 1; chop.ExpectedPieces = 3; }
            if (target is MooseCorpseState corpse)
            {
                corpse.Corpse = 5; corpse.Dead = true; corpse.FrontPieces = 2; corpse.RearPieces = 4;
                corpse.Positions = new NetVector3[11]; corpse.Rotations = new NetQuaternion[11];
                for (int i = 0; i < 11; i++) { corpse.Positions[i] = new NetVector3(i, i * 2, -i); corpse.Rotations[i] = NetQuaternion.Identity; }
            }
            if (target is MooseMeatState meat) { meat.FactoryId = 42; meat.NativeId = "moosemeat01"; meat.Condition = 40; meat.Kind = 2; meat.Rotation = NetQuaternion.Identity; }
            if (target is MilkConditionState milk) { milk.NetId = 42; milk.Condition = 87; milk.Spoiled = 0; }
            if (target is UtilityPaymentIntent utilityIntent) { utilityIntent.PlayerId = 1; utilityIntent.Meter = 1; }
            if (target is UtilityPaymentResult utilityResult) { utilityResult.PlayerId = 1; utilityResult.Meter = 1; utilityResult.Result = 0; utilityResult.Paid = 123.25f; }
            if (target is UtilityBillState utility) { utility.Meter = 1; utility.UnpaidBills = 12.375f; utility.Flags = 6; utility.Phone = null; }
            if (target is CylinderHeadState head) { head.NetId = 42; head.ParentId = 43; head.Mass = 0; head.Rotation = NetQuaternion.Identity; }
            if (target is ValveAdjustmentState valve) { valve.NetId = 42; valve.Setting = 4.137f; }
            if (target is VehicleDrivetrainWearState drivetrain) drivetrain.Flags = VehicleDrivetrainWearState.Available;
            if (target is EngineBlockState block) { block.Flags = 63; block.Wear = 19.75f; block.FuelChamber = 25; block.CarbReserve = .1f; block.SettingMixture = 14.5f; block.CarburettorPower = 140; block.CarburettorTorque = 210; block.CarburettorPowerAdd = .07f; block.AirCleanerPower = 100; block.AirCleanerTorque = 250; block.AirCleanerPowerAdd = -.04f; block.CoolingAmbientAvailable = true; block.CoolingAmbientTemperature = -12.5f; block.CoolingAirflowFlags = 15; block.GrilleAirflow = -40; block.HoodAirflow = 900; block.FiberglassHoodAirflow = 700; block.CoolantHoseFlags = 15; block.CoolantHoseTightness = new float[] { 4, 8, 12, 16 }; block.CarburettorTightness = 32; block.RadiatorInstalled = true; block.RadiatorWear = 83; block.RadiatorCoolant = 5.3f; block.RadiatorPressureCap = 16; block.RadiatorFlectEfficiency = 2.1f; block.RockerCoverInstalled = true; block.RockerCoverTightness = 63.5f; block.OilpanInstalled = true; block.OilpanWear = 88; block.OilpanTightness = 76; block.Oil = 3.6f; block.OilContamination = .9f; block.OilViscosity = 12; block.ValvesAvailable = true; block.ValveSettings = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }; block.ExhaustFlags = 15; block.ExhaustPerformance = new float[] { 11, 101, -.01f, 22, 202, .02f, 33, 303, -.03f, 44, 404, .04f }; }
            if (target is VehicleCondition condition) { condition.Availability = VehicleCondition.AvailableAll; condition.Flags = 0x18; }
            if (target is VehicleConditionReleaseAck release) { release.Condition.OwnerPlayerId = 1; release.Condition.Sequence = 8; }
            if (target is VehicleClimate climate)
            {
                climate.IceMask = VehicleClimate.AllWindows;
                climate.ParkingBrakeAvailable = true; climate.ParkingBrake = .625f;
            }
            if (target is PlayerTransform player) { player.HasSweat = true; player.Sweat = 23.75f; }
            if (target is PlayerNeedsReport needs) needs.HasBodyTemp = true;
            if (target is GuestSpawn spawn) spawn.Flags |= GuestSpawn.FlagHasSavedNeeds | GuestSpawn.FlagHasSavedBodyTemp;
            if (target is HeaterState heater) { heater.Flags = 3; heater.Wear = 27.75f; heater.RearWindowFlags = 1; }
            if (target is BatteryState battery) { battery.Flags = 3; battery.Charge = 126.25f; battery.ChargeMax = 147.5f; }
            if (target is WheelPunctureRequest puncture) { puncture.VehicleId = 42; puncture.PlayerId = 1; puncture.Wheel = 0; puncture.Epoch = 1; }
            if (target is StoveKnobIntent stoveTurn) { stoveTurn.ApplianceId = 42; stoveTurn.PlayerId = 1; stoveTurn.Plate = 2; stoveTurn.Direction = 1; }
            if (target is AtfBottleState atfBottle) { atfBottle.ItemId = 42; atfBottle.Revision = 7; atfBottle.NativeId = "atfoil012"; atfBottle.Fluid = .6f; atfBottle.Empty = false; atfBottle.Rotation = NetQuaternion.Identity; }
            if (target is AtfFillerState atfFiller) { atfFiller.VehicleId = 43; atfFiller.Revision = 7; atfFiller.Rotation = 359; atfFiller.OilLevel = 4.2f; atfFiller.Flags = AtfFillerState.FlagAvailable;
                atfFiller.CapLocalPosition = new NetVector3(.1f, .25f, 1.1f); atfFiller.CapLocalRotation = NetQuaternion.Identity; }
            if (target is FleaListingIntent listingIntent) { listingIntent.PlayerId = 1; listingIntent.ItemId = 5; listingIntent.Price = 5; }
            if (target is FleaListingResult listingResult) { listingResult.PlayerId = 1; listingResult.ItemId = 5; listingResult.Result = 0; }
            if (target is FleaSaleState flea) { flea.MoneyTotal = 100; flea.RentDays = 7; flea.Flags = 1; flea.WeekPrice = 150; }
            if (target is FleaSaleIntent fleaIntent) { fleaIntent.PlayerId = 1; fleaIntent.Action = FleaSaleIntent.PayRent; fleaIntent.Weeks = 1; }
            if (target is FleaSaleResult fleaResult) { fleaResult.PlayerId = 1; fleaResult.Action = FleaSaleIntent.PayRent; fleaResult.Result = FleaSaleResult.Accepted; }
            if (target is AtfRefillIntent atfIntent) { atfIntent.VehicleId = 43; atfIntent.BottleId = 42; atfIntent.PlayerId = 1; atfIntent.Action = AtfRefillIntent.Pour; }
            if (target is ApplianceState stove) { stove.Kind = 0; stove.Flags = 15; stove.FirePlate = 2; stove.StoveRevision = 7; stove.StoveHeat = new float[] { 23, 151.5f, 400, 750 }; stove.StoveRotation = new float[] { 0, -51.4f, 102.8f, 308.4f }; stove.GrillMask = 2; stove.BurnMask = 12; }
            if (target is GearboxWearRequest gearboxWear) { gearboxWear.VehicleId = 42; gearboxWear.PlayerId = 1; }
            if (target is GearboxOilUseRequest oilUse) { oilUse.VehicleId = 42; oilUse.PlayerId = 1; oilUse.Phase = 1; }
            if (target is StarterWearRequest wear) { wear.VehicleId = 42; wear.PlayerId = 1; wear.Seconds = .25f; }
            if (target is StarterDrawRequest draw) { draw.VehicleId = 42; draw.PlayerId = 1; draw.Kind = StarterDrawRequest.Loaded; draw.Count = 17; }
            if (target is WiringState wiring) { wiring.SourceId = 3; wiring.Flags = WiringState.Available | WiringState.Installed | WiringState.Bolted; }
            if (target is PartBeltVisualState visual)
            { visual.Scale = .8f; visual.Pitch = 1.4f; visual.Volume = .6f; visual.ScrollSpeed = -123.5f; }
        }

        private static object MakeValue(Type t, int seed, int arrayLength = 2)
        {
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null) return MakeValue(underlying, seed, arrayLength);
            if (t.IsEnum) { var values = Enum.GetValues(t); return values.GetValue(seed % values.Length)!; }
            if (t == typeof(byte)) return (byte)(seed & 0x7F | 1);
            if (t == typeof(sbyte)) return (sbyte)(seed & 0x3F | 1);
            if (t == typeof(bool)) return true;
            if (t == typeof(ushort)) return (ushort)(seed * 131 + 7);
            if (t == typeof(short)) return (short)(seed * 131 + 7);
            if (t == typeof(uint)) return (uint)(seed * 2654435761u + 11u);
            if (t == typeof(int)) return seed * 40503 + 13;
            if (t == typeof(ulong)) return (ulong)seed * 0x9E3779B97F4A7C15UL + 17UL;
            if (t == typeof(long)) return (long)seed * 0x123456789L + 19L;
            if (t == typeof(float)) return seed * 1.5f + 0.25f;
            if (t == typeof(double)) return seed * 1.5 + 0.25;
            if (t == typeof(string)) return "str" + seed;
            if (t == typeof(NetVector3)) return new NetVector3(seed + 0.1f, seed + 0.2f, seed + 0.3f);
            if (t == typeof(NetQuaternion)) return new NetQuaternion(seed + 0.1f, seed + 0.2f, seed + 0.3f, seed + 0.4f);
            if (t == typeof(VenttiPose))
            {
                // Scene packets enforce unit quaternions and a 0/1 visibility flag.
                var q = (NetQuaternion)MakeValue(typeof(NetQuaternion), seed);
                float norm = (float)Math.Sqrt((double)q.X*q.X + (double)q.Y*q.Y + (double)q.Z*q.Z + (double)q.W*q.W);
                return new VenttiPose { Position = (NetVector3)MakeValue(typeof(NetVector3), seed),
                    Rotation = new NetQuaternion(q.X/norm, q.Y/norm, q.Z/norm, q.W/norm), Active = (byte)(seed & 1) };
            }
            if (t.IsEnum)
            {
                var values = Enum.GetValues(t);
                // Prefer a non-zero defined value so a field that is written survives the trip.
                foreach (var v in values)
                    if (Convert.ToInt64(v) != 0) return v;
                return (values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(t))
                    ?? throw new InvalidOperationException("Cannot create enum " + t.Name);
            }

            if (t.IsArray)
            {
                var elem = t.GetElementType()!;
                var arr = Array.CreateInstance(elem, arrayLength);
                for (int i = 0; i < arrayLength; i++)
                    arr.SetValue(MakeValue(elem, seed + i * 100), i);
                return arr;
            }

            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t)!;
                list.Add(MakeValue(elem, seed));
                list.Add(MakeValue(elem, seed + 100));
                return list;
            }

            // Nested struct/class (message Entry types): construct and fill recursively.
            object nested = Activator.CreateInstance(t)!;
            Fill(nested, seed);
            return nested;
        }
    }
}
