using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinterMP.Core.Catalog
{
    /// <summary>Minimal JSON reader for sync-catalog.json (objects, arrays, strings only).</summary>
    internal static partial class SyncCatalogJson
    {
        public static SyncCatalogData Parse(string json)
        {
            var reader = new Reader(json ?? string.Empty);
            reader.SkipWhitespace();
            var root = reader.ReadObject();
            return ToData(root);
        }

        private static SyncCatalogData ToData(Dictionary<string, object?> root)
        {
            var data = new SyncCatalogData();
            if (root.TryGetValue("gameBuild", out var build) && build is string buildText)
                data.GameBuild = buildText;

            if (root.TryGetValue("fleaSale", out var fleaSale))
            {
                try { data.FleaSale = ParseFleaSale(fleaSale); }
                catch (FormatException e) { data.FleaSaleError = e.Message; }
            }
            ParseRuleArray(root, "doors", data.Doors);
            ParseRuleArray(root, "spawnContainers", data.SpawnContainers);
            if (root.TryGetValue("trophyFactories", out var factories))
                data.TrophyFactories = ParseTrophyFactories(factories);
            if (root.TryGetValue("partsPackages", out var packages))
                data.PartsPackages = ParsePartsPackages(packages);
            if (root.TryGetValue("shoppingBags", out var bags))
                data.ShoppingBags = ParseShoppingBags(bags);
            if (root.TryGetValue("partIdentity", out var partIdentity))
                data.PartIdentity = ParsePartIdentity(partIdentity);
            if (root.TryGetValue("replacementParts", out var replacements))
                data.ReplacementParts = ParseReplacementParts(replacements);
            ParseRuleArray(root, "controls", data.Controls);
            ParseRuleArray(root, "switchRules", data.SwitchRules);
            ParseRuleArray(root, "ignitions", data.Ignitions);
            ParseRuleArray(root, "starters", data.Starters);
            ParseBuyArray(root, "buys", data.Buys);
            ParsePartArray(root, "parts", data.Parts);
            ParseBoltArray(root, "bolts", data.Bolts);
            if (root.TryGetValue("firewoodDelivery", out var firewoodDelivery))
            {
                try { data.FirewoodDelivery = ParseFirewoodDelivery(firewoodDelivery); }
                catch (FormatException e) { data.FirewoodDeliveryError = e.Message; }
            }
            if (root.TryGetValue("motorOil", out var motorOil))
            {
                try { data.MotorOil = ParseMotorOil(motorOil); }
                catch (FormatException e) { data.MotorOilError = e.Message; }
            }
            if (root.TryGetValue("advertPhone", out var advertPhone))
            {
                try { data.AdvertPhone = ParseAdvertPhone(advertPhone); }
                catch (FormatException e) { data.AdvertPhoneError = e.Message; }
            }
            if (root.TryGetValue("adverts", out var adverts))
            {
                try { data.Adverts = ParseAdverts(adverts); }
                catch (FormatException e) { data.AdvertsError = e.Message; }
            }
            if (root.TryGetValue("train", out var train))
            {
                try { data.Train = ParseTrain(train); }
                catch (FormatException e) { data.TrainError = e.Message; }
            }
            if (root.TryGetValue("coffee", out var coffee))
            {
                try { data.Coffee = ParseCoffee(coffee); }
                catch (FormatException e) { data.CoffeeError = e.Message; }
            }
            if (root.TryGetValue("taxiPassengers", out var taxiPassengers))
            {
                try { data.TaxiPassengers = ParseTaxiPassengers(taxiPassengers); }
                catch (FormatException e) { data.TaxiPassengersError = e.Message; }
            }
            if (root.TryGetValue("sausages", out var sausages))
            {
                try { data.Sausages = ParseSausages(sausages); }
                catch (FormatException e) { data.SausagesError = e.Message; }
            }
            if (root.TryGetValue("tractorTrailer", out var tractorTrailer))
            {
                try { data.TractorTrailer = ParseTractorTrailer(tractorTrailer); }
                catch (FormatException e) { data.TractorTrailerError = e.Message; }
            }
            if (root.TryGetValue("householdFuses", out var householdFuses))
            {
                try { data.HouseholdFuses = ParseHouseholdFuses(householdFuses); }
                catch (FormatException e) { data.HouseholdFusesError = e.Message; }
            }
            if (root.TryGetValue("taxiFare", out var taxiFare))
            {
                try { data.TaxiFare = ParseTaxiFare(taxiFare); }
                catch (FormatException e) { data.TaxiFareError = e.Message; }
            }
            if (root.TryGetValue("taxiMeter", out var taxiMeter))
            {
                try { data.TaxiMeter = ParseTaxiMeter(taxiMeter); }
                catch (FormatException e) { data.TaxiMeterError = e.Message; }
            }
            if (root.TryGetValue("taxiService", out var taxiService))
            {
                try { data.TaxiService = ParseTaxiService(taxiService); }
                catch (FormatException e) { data.TaxiServiceError = e.Message; }
            }
            if (root.TryGetValue("taxiPickup", out var taxiPickup))
            {
                try { data.TaxiPickup = ParseTaxiPickup(taxiPickup); }
                catch (FormatException e) { data.TaxiPickupError = e.Message; }
            }
            if (root.TryGetValue("firewoodBuyers", out var firewoodBuyers))
            {
                try { data.FirewoodBuyers = ParseFirewoodBuyers(firewoodBuyers); }
                catch (FormatException e) { data.FirewoodBuyersError = e.Message; }
            }
            if (root.TryGetValue("utilityPayments", out var utilityPayments))
            {
                try { data.UtilityPayments = ParseUtilityPayments(utilityPayments); }
                catch (FormatException e) { data.UtilityPaymentsError = e.Message; }
            }
            if (root.TryGetValue("phonePayments", out var phonePayments))
            {
                try { data.PhonePayments = ParseUtilityPayments(phonePayments, true); }
                catch (FormatException e) { data.PhonePaymentsError = e.Message; }
            }
            if (root.TryGetValue("mooseChop", out var mooseChop))
            {
                try { data.MooseChop = ParseMooseChop(mooseChop); }
                catch (FormatException e) { data.MooseChopError = e.Message; }
            }
            if (root.TryGetValue("mooseMeat", out var mooseMeat))
            {
                try { data.MooseMeat = ParseMooseMeat(mooseMeat); }
                catch (FormatException e) { data.MooseMeatError = e.Message; }
            }
            if (root.TryGetValue("milkCondition", out var milkCondition))
            {
                try { data.MilkCondition = ParseMilkCondition(milkCondition); }
                catch (FormatException e) { data.MilkConditionError = e.Message; }
            }
            if (root.TryGetValue("cylinderHead", out var cylinderHead))
            {
                try { data.CylinderHead = ParseCylinderHead(cylinderHead); }
                catch (FormatException e) { data.CylinderHeadError = e.Message; }
            }
            if (root.TryGetValue("valveAdjustment", out var valveAdjustment))
            {
                try { data.ValveAdjustment = ParseValveAdjustment(valveAdjustment); }
                catch (FormatException e) { data.ValveAdjustmentError = e.Message; }
            }
            if (root.TryGetValue("vehicles", out var vehiclesObj) && vehiclesObj is Dictionary<string, object?> vehicles)
                data.Vehicles = ParseVehicles(vehicles);
            if (root.TryGetValue("pickables", out var pickablesObj) && pickablesObj is Dictionary<string, object?> pickables)
                data.Pickables = ParsePickables(pickables);
            if (root.TryGetValue("consumables", out var consumablesObj) && consumablesObj is Dictionary<string, object?> consumables)
                data.Consumables = ParseConsumables(consumables);
            if (root.TryGetValue("vehicleClimate", out var climateObj) && climateObj is Dictionary<string, object?> climate)
                data.VehicleClimate = ParseVehicleClimate(climate);
            if (root.TryGetValue("banking", out var bankObj) && bankObj is Dictionary<string, object?> bank)
                data.Banking = ParseBanking(bank);
            if (root.TryGetValue("vehicleDamage", out var damageObj) && damageObj is Dictionary<string, object?> damage)
                data.VehicleDamage = ParseVehicleDamage(damage);
            if (root.TryGetValue("guestEngineProtection", out var engineProtection))
            {
                // Malformed local protection metadata must not discard unrelated sync rules.
                try { data.GuestEngineProtection = ParseGuestEngineProtection(engineProtection); }
                catch (FormatException e) { data.GuestEngineProtectionError = e.Message; }
            }
            if (root.TryGetValue("vehicleElectrical", out var electrical))
            {
                try { data.VehicleElectrical = ParseVehicleElectrical(electrical); }
                catch (FormatException e) { data.VehicleElectricalError = e.Message; }
            }
            if (root.TryGetValue("vehicleCooling", out var speed))
            {
                try { data.VehicleCooling = ParseVehicleCooling(speed); }
                catch (FormatException e) { data.VehicleCoolingError = e.Message; }
            }
            if (root.TryGetValue("vehicleDrivetrainWear", out var drivetrainWear))
            {
                try { data.VehicleDrivetrainWear = ParseVehicleDrivetrainWear(drivetrainWear); }
                catch (FormatException e) { data.VehicleDrivetrainWearError = e.Message; }
            }
            if (root.TryGetValue("vehicleDifferentialSpeed", out var differential))
            {
                try { data.VehicleDifferentialSpeed = ParseVehicleDifferentialSpeed(differential); }
                catch (FormatException e) { data.VehicleDifferentialSpeedError = e.Message; }
            }
            if (root.TryGetValue("vehicleHeat", out var heat))
            {
                try { data.VehicleHeat = ParseVehicleHeat(heat); }
                catch (FormatException e) { data.VehicleHeatError = e.Message; }
            }
            if (root.TryGetValue("vehicleWearInputs", out var wearInputs))
            {
                try { data.VehicleWearInputs = ParseVehicleWearInputs(wearInputs); }
                catch (FormatException e) { data.VehicleWearInputsError = e.Message; }
            }
            if (root.TryGetValue("vehicleTemperature", out var temperature))
            {
                try { data.VehicleTemperature = ParseVehicleTemperature(temperature); }
                catch (FormatException e) { data.VehicleTemperatureError = e.Message; }
            }
            if (root.TryGetValue("vehicleWheelHealth", out var wheelHealth))
            {
                try { data.VehicleWheelHealth = ParseVehicleWheelHealth(wheelHealth); }
                catch (FormatException e) { data.VehicleWheelHealthError = e.Message; }
            }
            if (root.TryGetValue("vehicleTirePressure", out var tirePressure))
            {
                try { data.VehicleTirePressure = ParseVehicleTirePressure(tirePressure); }
                catch (FormatException e) { data.VehicleTirePressureError = e.Message; }
            }
            if (root.TryGetValue("vehicleEngineRpm", out var engineRpm))
            {
                try { data.VehicleEngineRpm = ParseVehicleEngineRpm(engineRpm); }
                catch (FormatException e) { data.VehicleEngineRpmError = e.Message; }
            }
            if (root.TryGetValue("vehicleParkingJoint", out var parkingJoint))
            {
                try { data.ParkingJoint = ParseParkingJoint(parkingJoint); }
                catch (FormatException e) { data.ParkingJointError = e.Message; }
            }
            if (root.TryGetValue("atfRefill", out var atfRefill))
            {
                try { data.AtfRefill = ParseAtfRefill(atfRefill); }
                catch (FormatException e) { data.AtfRefillError = e.Message; }
            }
            if (root.TryGetValue("stoves", out var stoves))
            {
                try { data.Stoves = ParseStoves(stoves); }
                catch (FormatException e) { data.StovesError = e.Message; }
            }
            if (root.TryGetValue("vehicleParkingBrake", out var parkingBrake))
            {
                try { data.ParkingBrake = ParseParkingBrake(parkingBrake); }
                catch (FormatException e) { data.ParkingBrakeError = e.Message; }
            }
            if (root.TryGetValue("vehicleEngineHandoff", out var engineHandoff))
            {
                try { data.VehicleEngineHandoff = ParseVehicleEngineHandoff(engineHandoff); }
                catch (FormatException e) { data.VehicleEngineHandoffError = e.Message; }
            }
            if (root.TryGetValue("guestEngineInputs", out var engineInputs))
            {
                try { data.GuestEngineInputs = ParseGuestEngineInputs(engineInputs, data.ReplacementParts, data.GuestEngineProtection); }
                catch (FormatException e) { data.GuestEngineInputsError = e.Message; }
            }
            if (root.TryGetValue("slotMachines", out var slotsObj) && slotsObj is Dictionary<string, object?> slots)
                data.SlotMachines = ParseSlotMachines(slots);
            if (root.TryGetValue("videoPoker", out var pokerObj) && pokerObj is Dictionary<string, object?> poker)
                data.VideoPoker = ParsePoker(poker);
            if (root.TryGetValue("venttiTable", out var tableObj) && tableObj is Dictionary<string, object?> table)
                data.VenttiTable = ParseVenttiTable(table);
            if (root.TryGetValue("hockeyBetting", out var hockeyObj))
            {
                if (!(hockeyObj is Dictionary<string, object?> hockey)) throw new FormatException("Invalid hockey bindings.");
                data.HockeyBetting = ParseHockeyBetting(hockey);
            }
            if (root.TryGetValue("lottoDraw", out var lottoObj))
            {
                if (!(lottoObj is Dictionary<string, object?> lotto)) throw new FormatException("Invalid Lotto draw bindings.");
                data.LottoDraw = ParseLottoDraw(lotto);
            }
            if (root.TryGetValue("lottoTickets", out var ticketsObj) && ticketsObj is Dictionary<string, object?> tickets)
            {
                data.LottoTickets = new LottoTicketsData();
                foreach (string key in LottoTicketsData.RequiredBindings)
                    data.LottoTickets.Bindings.Add(key, RequiredString(tickets, key));
                data.LottoTickets.LinePrice = SlotNumber(tickets.TryGetValue("linePrice", out var price) ? price : null, "linePrice", 1, 1000);
                data.LottoTickets.BankThreshold = SlotNumber(tickets.TryGetValue("bankThreshold", out var threshold) ? threshold : null, "bankThreshold", 1, 1000000);
            }
            if (root.TryGetValue("rallyProgress", out var rallyObj) && rallyObj is Dictionary<string, object?> rally)
                data.RallyProgress = ParseRallyProgress(rally);
            if (root.TryGetValue("debtLetter", out var debtObj) && debtObj is Dictionary<string, object?> debt)
            {
                data.DebtLetter = new DebtLetterData();
                foreach (string key in DebtLetterData.RequiredBindings)
                    data.DebtLetter.Bindings.Add(key, RequiredString(debt, key));
            }
            if (root.TryGetValue("venttiProperty", out var propertyObj) && propertyObj is Dictionary<string, object?> property)
            {
                data.VenttiProperty = new VenttiPropertyData();
                foreach (string key in VenttiPropertyData.RequiredBindings)
                    data.VenttiProperty.Bindings.Add(key, RequiredString(property, key));
                var bindings = data.VenttiProperty;
                if (new HashSet<string> { bindings["rusckoKey"], bindings["satsumaKey"], bindings["homeKey"] }.Count != 3
                    || new HashSet<string> { bindings["sleepPath"], bindings["hatchPath"], bindings["loggingPath"] }.Count != 3)
                    throw new FormatException("Ventti property slots must have distinct bindings.");
                var tableBindings = data.VenttiTable;
                if (tableBindings != null && (bindings["managerPath"] != tableBindings["tablePath"] + "/" + tableBindings["managerPath"]
                    || bindings["managerFsm"] != tableBindings["fsm"]))
                    throw new FormatException("Ventti table and property bindings must use the same resolver.");
            }
            return data;
        }

        private static string RequiredString(Dictionary<string, object?> obj, string key)
        {
            string value = GetString(obj, key);
            if (value.Length == 0) throw new FormatException("Missing catalog binding: " + key);
            return value;
        }

        private static bool ValidScenePath(string path) => path.Length > 0 && !path.StartsWith("/", StringComparison.Ordinal)
            && !path.EndsWith("/", StringComparison.Ordinal) && path.IndexOf("//", StringComparison.Ordinal) < 0
            && path.IndexOf("..", StringComparison.Ordinal) < 0 && path.IndexOfAny(new[] { '\n', '\r', '\0', '\\' }) < 0;

        private static RallyProgressData ParseRallyProgress(Dictionary<string, object?> obj)
        {
            var data = new RallyProgressData
            {
                TimingFsm = RequiredString(obj, "timingFsm"), StartedVariable = RequiredString(obj, "startedVariable"),
                MarkerFsm = RequiredString(obj, "markerFsm"), CommitState = RequiredString(obj, "commitState"),
                CompletedState = RequiredString(obj, "completedState"),
            };
            if (data.CommitState == data.CompletedState || !obj.TryGetValue("stages", out var entries)
                || entries is not List<object?> stages || stages.Count != 3)
                throw new FormatException("Three distinct rally stage bindings are required.");
            var paths = new HashSet<string>();
            foreach (var entry in stages)
            {
                if (entry is not Dictionary<string, object?> stage || !stage.TryGetValue("checkpoints", out var pointsObj)
                    || pointsObj is not List<object?> points || points.Count < 1 || points.Count > 6)
                    throw new FormatException("Invalid rally checkpoints.");
                var binding = new RallyStageData { TimingPath = RequiredString(stage, "timingPath"),
                    StartPath = RequiredString(stage, "startPath"), Checkpoints = SlotStrings(stage, "checkpoints", points.Count) };
                if (!ValidScenePath(binding.TimingPath) || !ValidScenePath(binding.StartPath)
                    || !paths.Add(binding.TimingPath) || !paths.Add(binding.StartPath))
                    throw new FormatException("Invalid/duplicate rally stage path.");
                foreach (string checkpoint in binding.Checkpoints)
                    if (!ValidScenePath(checkpoint) || checkpoint.IndexOf('/') >= 0
                        || checkpoint == data.StartedVariable || !paths.Add(binding.TimingPath + "/" + checkpoint))
                        throw new FormatException("Invalid/duplicate rally checkpoint binding.");
                data.Stages.Add(binding);
            }
            return data;
        }

        private static BankingData ParseBanking(Dictionary<string, object?> obj)
        {
            var data = new BankingData
            {
                BankPath = RequiredString(obj, "bankPath"), BankFsm = RequiredString(obj, "bankFsm"),
                AtmPath = RequiredString(obj, "atmPath"), AtmFsm = RequiredString(obj, "atmFsm"),
                CashPath = RequiredString(obj, "cashPath"), CashFsm = RequiredString(obj, "cashFsm"),
                CashGlobal = RequiredString(obj, "cashGlobal"), BankGlobal = RequiredString(obj, "bankGlobal"),
                IncomeGlobal = RequiredString(obj, "incomeGlobal"),
            };
            if (!obj.TryGetValue("mutations", out var entries) || entries is not List<object?> mutations)
                throw new FormatException("Banking mutations are required.");
            foreach (var entry in mutations)
            {
                if (entry is not Dictionary<string, object?> mutation)
                    throw new FormatException("Invalid banking mutation.");
                var binding = new BankMutationData
                {
                    Target = RequiredString(mutation, "target"), State = RequiredString(mutation, "state"),
                    ActionType = RequiredString(mutation, "actionType"),
                    Balance = RequiredString(mutation, "balance"),
                    AmountVariable = GetString(mutation, "amountVariable"),
                    Direction = GetFloat(mutation, "direction", 0f),
                };
                if ((binding.Target != "atm" && binding.Target != "cash")
                    || (binding.Balance != "cash" && binding.Balance != "bank")
                    || (binding.Direction != 0 && binding.Direction != 1 && binding.Direction != -1)
                    || (binding.Direction != 0 && binding.AmountVariable.Length == 0))
                    throw new FormatException("Invalid banking transfer binding.");
                foreach (var previous in data.Mutations)
                    if (previous.Target == binding.Target && previous.State == binding.State)
                        throw new FormatException("Duplicate banking mutation state.");
                data.Mutations.Add(binding);
            }
            if (data.Mutations.Count == 0) throw new FormatException("Banking mutations cannot be empty.");
            return data;
        }

        private static VehicleDamageData ParseVehicleDamage(Dictionary<string, object?> obj)
        {
            var data = new VehicleDamageData
            {
                ObjectName = RequiredString(obj, "objectName"), FsmName = RequiredString(obj, "fsmName"),
                IdleState = RequiredString(obj, "idleState"), PartFsmName = RequiredString(obj, "partFsmName"),
                WearVariable = RequiredString(obj, "wearVariable"),
            };
            ReadDamageSlots(obj, "events", data.Events);
            ReadDamageSlots(obj, "partVariables", data.PartVariables);
            if (data.Events.Count != 16 || data.PartVariables.Count != 16)
                throw new FormatException("Vehicle damage requires 16 fixed protocol slots.");
            for (int i = 0; i < 16; i++)
            {
                bool retired = i == 13 || i == 15;
                if ((data.Events[i].Length == 0) != retired || (data.PartVariables[i].Length == 0) != retired)
                    throw new FormatException("Vehicle damage slots 13/15 are retired; concrete slots require bindings.");
                if (data.Events[i] == "SEIZE" || data.Events[i] == "CAMFAIL")
                    throw new FormatException("Random damage selectors cannot be replay events.");
            }
            return data;
        }

        private static void ReadDamageSlots(Dictionary<string, object?> obj, string key, List<string> target)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> slots)
                throw new FormatException("Missing damage slots: " + key);
            foreach (var slot in slots)
            {
                if (slot is not string text) throw new FormatException("Damage slots must be strings.");
                // Empty retired slots must retain their protocol bit positions.
                target.Add(text);
            }
        }

        private static VehicleRegistrationData ParseVehicles(Dictionary<string, object?> obj)
        {
            var data = new VehicleRegistrationData
            {
                MinMass = GetFloat(obj, "minMass", 150f),
                RequireRoot = GetBool(obj, "requireRoot", true),
            };
            AppendStrings(obj, "namePrefixes", data.NamePrefixes);
            return data;
        }

        private static PickableRegistrationData ParsePickables(Dictionary<string, object?> obj)
        {
            var data = new PickableRegistrationData
            {
                ProbeUseFsm = GetBool(obj, "probeUseFsm", true),
            };
            AppendStrings(obj, "excludeNameContains", data.ExcludeNameContains);
            AppendStrings(obj, "nameSuffixes", data.NameSuffixes);
            return data;
        }

        private static ConsumableData ParseConsumables(Dictionary<string, object?> obj)
        {
            var data = new ConsumableData
            {
                FsmName = GetString(obj, "fsmName"),
                DrinkCheckState = GetString(obj, "drinkCheckState"),
            };
            if (data.FsmName.Length == 0) data.FsmName = "Use";
            if (data.DrinkCheckState.Length == 0) data.DrinkCheckState = "Check drink";
            AppendStrings(obj, "destroyStates", data.DestroyStates);
            AppendStrings(obj, "drinkEmptyStates", data.DrinkEmptyStates);
            return data;
        }

        private static VehicleClimateData ParseVehicleClimate(Dictionary<string, object?> obj)
        {
            var data = new VehicleClimateData();
            AppendStrings(obj, "pathPrefixes", data.PathPrefixes);
            AppendStrings(obj, "carTempPathContains", data.CarTempPathContains);
            AppendStrings(obj, "heaterPathContains", data.HeaterPathContains);
            if (obj.TryGetValue("passengerHeating", out var heating))
            {
                try
                {
                    if (heating is not Dictionary<string, object?> p) throw new FormatException("Invalid passenger heating profile.");
                    data.PassengerHeating = new PassengerHeatingData {
                        BodyPath = TemperaturePath(p, "bodyPath"), BodyFsm = TemperatureName(p, "bodyFsm"),
                        AmbientState = TemperatureName(p, "ambientState"), HeatState = TemperatureName(p, "heatState"),
                        RainPath = TemperaturePath(p, "rainPath"), RainVariable = TemperatureName(p, "rainVariable"),
                        RainFsm = TemperatureName(p, "rainFsm"), HeatVariable = TemperatureName(p, "heatVariable"),
                        HeatFsm = TemperatureName(p, "heatFsm"), TemperatureVariable = TemperatureName(p, "temperatureVariable") };
                }
                catch (FormatException error) { data.PassengerHeatingError = error.Message; }
            }
            if (obj.TryGetValue("passengerCondensation", out var condensation))
            {
                try
                {
                    if (condensation is not Dictionary<string, object?> p) throw new FormatException("Invalid passenger condensation profile.");
                    data.PassengerCondensation = new PassengerCondensationData {
                        State = TemperatureName(p, "state"), EntryVariable = TemperatureName(p, "entryVariable"),
                        SweatGlobal = TemperatureName(p, "sweatGlobal"), SweatVariable = TemperatureName(p, "sweatVariable"),
                        RateVariable = TemperatureName(p, "rateVariable"), DefaultRate = TemperatureName(p, "defaultRate"),
                        DefrostingRate = TemperatureName(p, "defrostingRate") };
                }
                catch (FormatException error) { data.PassengerCondensationError = error.Message; }
            }
            return data;
        }

        private static float GetFloat(Dictionary<string, object?> obj, string key, float defaultValue)
        {
            if (!obj.TryGetValue(key, out var value) || value == null) return defaultValue;
            if (value is double d) return (float)d;
            if (value is long l) return l;
            return defaultValue;
        }

        private static bool GetBool(Dictionary<string, object?> obj, string key, bool defaultValue)
        {
            if (!obj.TryGetValue(key, out var value)) return defaultValue;
            return value is bool b ? b : defaultValue;
        }

        private static void ParseBuyArray(
            Dictionary<string, object?> root,
            string key,
            List<BuyRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParseBuyRule(obj);
                if (rule.FsmName.Length == 0) continue;
                if (rule.Template == "shopBuy")
                {
                    target.Add(rule);
                    continue;
                }

                if (rule.EntryGuards.Count > 0 && rule.ResultStates.Count > 0)
                    target.Add(rule);
            }
        }

        private static void ParsePartArray(
            Dictionary<string, object?> root,
            string key,
            List<PartRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParsePartRule(obj);
                if (rule.FsmName.Length > 0 && rule.States.Count > 0)
                    target.Add(rule);
            }
        }

        private static PartRuleData ParsePartRule(Dictionary<string, object?> obj)
        {
            var rule = new PartRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
            };

            AppendStrings(obj, "states", rule.States);
            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "optionalStates", rule.OptionalStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);
            return rule;
        }

        private static void ParseBoltArray(
            Dictionary<string, object?> root,
            string key,
            List<BoltRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParseBoltRule(obj);
                if (rule.FsmName.Length > 0 && rule.RequireStates.Count > 0)
                    target.Add(rule);
            }
        }

        private static BoltRuleData ParseBoltRule(Dictionary<string, object?> obj)
        {
            var rule = new BoltRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
            };

            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);
            return rule;
        }

        private static BuyRuleData ParseBuyRule(Dictionary<string, object?> obj)
        {
            var rule = new BuyRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
                Template = GetOptionalString(obj, "template") ?? string.Empty,
            };

            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "resultStates", rule.ResultStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);

            if (!obj.TryGetValue("entryGuards", out var guardsObj) || guardsObj is not List<object?> guards)
                return rule;

            foreach (var guardObj in guards)
            {
                if (guardObj is not Dictionary<string, object?> guardDict) continue;
                string state = GetString(guardDict, "state");
                string trigger = GetString(guardDict, "event");
                if (state.Length == 0 || trigger.Length == 0) continue;
                rule.EntryGuards.Add(new BuyGuardData
                {
                    StateName = state,
                    TriggerEvent = trigger,
                    Optional = GetBool(guardDict, "optional"),
                });
            }

            return rule;
        }

        private static bool GetBool(Dictionary<string, object?> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value)) return false;
            return value is bool b && b;
        }

        private static void ParseRuleArray(
            Dictionary<string, object?> root,
            string key,
            List<CatalogRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParseRule(obj);
                if (rule.FsmName.Length > 0 && rule.States.Count > 0)
                    target.Add(rule);
            }
        }

        private static CatalogRuleData ParseRule(Dictionary<string, object?> obj)
        {
            var rule = new CatalogRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
                ScalarFloatName = GetOptionalString(obj, "scalarFloat"),
                ScalarCommitState = GetOptionalString(obj, "scalarCommitState"),
                HostPayment = GetOptionalString(obj, "hostPayment"),
            };

            AppendStrings(obj, "states", rule.States);
            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);
            if (obj.ContainsKey("hostPayment") && (rule.HostPayment != "firewood" || rule.ScalarFloatName != null
                || rule.States.Count != 1 || rule.States[0] != "State 1" || rule.FsmName != "Use"))
                throw new FormatException("Unsupported host payment control.");
            return rule;
        }

        private static void AppendStrings(Dictionary<string, object?> obj, string key, List<string> target)
        {
            if (!obj.TryGetValue(key, out var valuesObj) || valuesObj is not List<object?> values)
                return;

            foreach (var value in values)
            {
                if (value is string text && text.Length > 0)
                    target.Add(text);
            }
        }

        private static string GetString(Dictionary<string, object?> obj, string key)
        {
            return obj.TryGetValue(key, out var value) && value is string text ? text : string.Empty;
        }

        private static string? GetOptionalString(Dictionary<string, object?> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || value is not string text || text.Length == 0)
                return null;
            return text;
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _index;

            internal Reader(string text)
            {
                _text = text;
            }

            internal void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                    _index++;
            }

            internal Dictionary<string, object?> ReadObject()
            {
                Expect('{');
                var obj = new Dictionary<string, object?>();
                SkipWhitespace();
                if (TryConsume('}')) return obj;

                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    Expect(',');
                }

                return obj;
            }

            internal List<object?> ReadArray()
            {
                Expect('[');
                var list = new List<object?>();
                SkipWhitespace();
                if (TryConsume(']')) return list;

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (TryConsume(']')) break;
                    Expect(',');
                }

                return list;
            }

            private object? ReadValue()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                    throw new FormatException("Unexpected end of JSON.");

                char c = _text[_index];
                if (c == '"') return ReadString();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == 'n' && MatchLiteral("null")) return null;
                if (c == 't' && MatchLiteral("true")) return true;
                if (c == 'f' && MatchLiteral("false")) return false;
                if (c == '-' || char.IsDigit(c)) return ReadNumber();
                throw new FormatException("Unsupported JSON value at " + _index + ".");
            }

            internal string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_index < _text.Length)
                {
                    char c = _text[_index++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (_index >= _text.Length)
                            throw new FormatException("Unterminated escape.");
                        char esc = _text[_index++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_index + 4 > _text.Length)
                                    throw new FormatException("Invalid unicode escape.");
                                sb.Append((char)Convert.ToInt32(_text.Substring(_index, 4), 16));
                                _index += 4;
                                break;
                            default: throw new FormatException("Invalid escape.");
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                throw new FormatException("Unterminated string.");
            }

            private object ReadNumber()
            {
                int start = _index;
                if (_text[_index] == '-') _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;
                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                        _index++;
                    return double.Parse(_text.Substring(start, _index - start), CultureInfo.InvariantCulture);
                }

                return long.Parse(_text.Substring(start, _index - start), CultureInfo.InvariantCulture);
            }

            private bool MatchLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length) return false;
                if (string.Compare(_text, _index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    return false;
                _index += literal.Length;
                return true;
            }

            private void Expect(char ch)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != ch)
                    throw new FormatException("Expected '" + ch + "' at " + _index + ".");
                _index++;
            }

            private bool TryConsume(char ch)
            {
                if (_index >= _text.Length || _text[_index] != ch) return false;
                _index++;
                return true;
            }
        }
    }

    internal sealed class SyncCatalogData
    {
        public string? GameBuild;
        public readonly List<CatalogRuleData> Doors = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> SpawnContainers = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> Controls = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> SwitchRules = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> Ignitions = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> Starters = new List<CatalogRuleData>();
        public readonly List<BuyRuleData> Buys = new List<BuyRuleData>();
        public readonly List<PartRuleData> Parts = new List<PartRuleData>();
        public readonly List<BoltRuleData> Bolts = new List<BoltRuleData>();
        public VehicleRegistrationData? Vehicles;
        public PickableRegistrationData? Pickables;
        public ConsumableData? Consumables;
        public VehicleClimateData? VehicleClimate;
        public BankingData? Banking;
        public VehicleDamageData? VehicleDamage;
        public GuestEngineProtectionData? GuestEngineProtection;
        public string? GuestEngineProtectionError;
        public VehicleElectricalData? VehicleElectrical;
        public string? VehicleElectricalError;
        public VehicleCoolingData? VehicleCooling;
        public string? VehicleCoolingError;
        public VehicleDrivetrainWearData? VehicleDrivetrainWear;
        public string? VehicleDrivetrainWearError;
        public VehicleDifferentialSpeedData? VehicleDifferentialSpeed;
        public string? VehicleDifferentialSpeedError;
        public VehicleHeatData? VehicleHeat;
        public string? VehicleHeatError;
        public VehicleWearInputData? VehicleWearInputs;
        public string? VehicleWearInputsError;
        public VehicleTemperatureData? VehicleTemperature;
        public string? VehicleTemperatureError;
        public VehicleWheelHealthData? VehicleWheelHealth;
        public string? VehicleWheelHealthError;
        public VehicleTirePressureData? VehicleTirePressure;
        public string? VehicleTirePressureError;
        public VehicleEngineRpmData? VehicleEngineRpm;
        public ParkingJointData? ParkingJoint;
        public AtfRefillData? AtfRefill;
        public string? AtfRefillError;
        public MotorOilData? MotorOil;
        public string? MotorOilError;
        public AdvertPhoneData? AdvertPhone;
        public string? AdvertPhoneError;
        public AdvertsData? Adverts;
        public string? AdvertsError;
        public TrainData? Train;
        public string? TrainError;
        public CoffeeData? Coffee;
        public string? CoffeeError;
        public TaxiPassengersData? TaxiPassengers;
        public string? TaxiPassengersError;
        public SausagesData? Sausages;
        public string? SausagesError;
        public TractorTrailerData? TractorTrailer;
        public string? TractorTrailerError;
        public HouseholdFuseData? HouseholdFuses;
        public string? HouseholdFusesError;
        public StoveData? Stoves;
        public string? StovesError;
        public string? ParkingJointError;
        public ParkingBrakeData? ParkingBrake;
        public string? ParkingBrakeError;
        public VehicleEngineHandoffData? VehicleEngineHandoff;
        public string? VehicleEngineHandoffError;
        public string? VehicleEngineRpmError;
        public GuestEngineInputsData? GuestEngineInputs;
        public string? GuestEngineInputsError;
        public SlotMachineData? SlotMachines;
        public PokerData? VideoPoker;
        public DebtLetterData? DebtLetter;
        public VenttiPropertyData? VenttiProperty;
        public VenttiTableData? VenttiTable;
        public RallyProgressData? RallyProgress;
        public LottoDrawData? LottoDraw;
        public HockeyBettingData? HockeyBetting;
        public LottoTicketsData? LottoTickets;
        public TrophyFactoriesData? TrophyFactories;
        public PartsPackagesData? PartsPackages;
        public ShoppingBagsData? ShoppingBags;
        public PartIdentityData? PartIdentity;
        public ReplacementPartsData? ReplacementParts;
        public ValveAdjustmentData? ValveAdjustment;
        public CylinderHeadData? CylinderHead;
        public FirewoodDeliveryData? FirewoodDelivery;
        public TaxiFareData? TaxiFare;
        public string? TaxiFareError;
        public TaxiMeterData? TaxiMeter;
        public string? TaxiMeterError;
        public TaxiServiceData? TaxiService;
        public string? TaxiServiceError;
        public TaxiPickupData? TaxiPickup;
        public string? TaxiPickupError;
        public string? FirewoodDeliveryError;
        public List<FirewoodBuyerData>? FirewoodBuyers;
        public string? FirewoodBuyersError;
        public FleaSaleData? FleaSale;
        public string? FleaSaleError;
        public UtilityPaymentsData? UtilityPayments, PhonePayments;
        public string? UtilityPaymentsError, PhonePaymentsError;
        public MooseChopData? MooseChop;
        public string? MooseChopError;
        public MooseMeatData? MooseMeat;
        public string? MooseMeatError;
        public MilkConditionData? MilkCondition;
        public string? MilkConditionError;
        public string? CylinderHeadError;
        public string? ValveAdjustmentError;
    }

    internal sealed class VenttiPropertyData
    {
        public static readonly string[] RequiredBindings = {
            "managerPath", "managerFsm", "rusckoKey", "satsumaKey", "homeKey",
            "sleepPath", "hatchPath", "loggingPath", "loggingFsm",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }

    internal sealed class DebtLetterData
    {
        public static readonly string[] RequiredBindings = {
            "rentPath", "rentFsm", "rentDebt", "rentEnvelope", "sheetPath", "setupFsm", "calculateState",
            "calculatedDebt", "originalText", "totalText", "interest", "cost1", "cost2",
            "payPath", "payFsm", "requestState", "commitState", "idleState", "fundsState", "closeState",
            "payTotal", "payDatabase", "payEnvelope", "paySheet",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }

    internal sealed class RallyProgressData
    {
        public string TimingFsm = "", StartedVariable = "", MarkerFsm = "", CommitState = "", CompletedState = "";
        public readonly List<RallyStageData> Stages = new List<RallyStageData>();
    }
    internal sealed class RallyStageData
    {
        public string TimingPath = "", StartPath = "";
        public string[] Checkpoints = new string[0];
    }

    internal sealed class BankingData
    {
        public string BankPath = string.Empty, BankFsm = string.Empty;
        public string AtmPath = string.Empty, AtmFsm = string.Empty;
        public string CashPath = string.Empty, CashFsm = string.Empty;
        public string CashGlobal = string.Empty, BankGlobal = string.Empty, IncomeGlobal = string.Empty;
        public readonly List<BankMutationData> Mutations = new List<BankMutationData>();
    }

    internal sealed class BankMutationData
    {
        public string Target = string.Empty, State = string.Empty, ActionType = string.Empty;
        public string AmountVariable = string.Empty, Balance = string.Empty;
        public float Direction;
    }

    internal sealed class VehicleDamageData
    {
        public string ObjectName = string.Empty, FsmName = string.Empty, IdleState = string.Empty;
        public string PartFsmName = string.Empty, WearVariable = string.Empty;
        public readonly List<string> Events = new List<string>();
        public readonly List<string> PartVariables = new List<string>();
    }

    internal sealed class VehicleRegistrationData
    {
        public float MinMass = 150f;
        public bool RequireRoot = true;
        public readonly List<string> NamePrefixes = new List<string>();
    }

    internal sealed class PickableRegistrationData
    {
        public bool ProbeUseFsm = true;
        public readonly List<string> ExcludeNameContains = new List<string>();
        public readonly List<string> NameSuffixes = new List<string>();
    }

    internal sealed class ConsumableData
    {
        public string FsmName = "Use";
        public string DrinkCheckState = "Check drink";
        public readonly List<string> DestroyStates = new List<string>();
        public readonly List<string> DrinkEmptyStates = new List<string>();
    }

    internal sealed class VehicleClimateData
    {
        public readonly List<string> PathPrefixes = new List<string>();
        public readonly List<string> CarTempPathContains = new List<string>();
        public readonly List<string> HeaterPathContains = new List<string>();
        public PassengerCondensationData? PassengerCondensation;
        public string? PassengerCondensationError;
        public PassengerHeatingData? PassengerHeating;
        public string? PassengerHeatingError;
    }

    internal sealed class PassengerHeatingData
    {
        public string BodyPath = "", BodyFsm = "", AmbientState = "", HeatState = "", RainPath = "";
        public string RainVariable = "", RainFsm = "", HeatVariable = "", HeatFsm = "", TemperatureVariable = "";
    }

    internal sealed class PassengerCondensationData
    {
        public string State = "", EntryVariable = "", SweatGlobal = "", SweatVariable = "";
        public string RateVariable = "", DefaultRate = "", DefrostingRate = "";
    }

    internal sealed class BuyRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public string Template = string.Empty;
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ResultStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
        public readonly List<BuyGuardData> EntryGuards = new List<BuyGuardData>();
    }

    internal sealed class BuyGuardData
    {
        public string StateName = string.Empty;
        public string TriggerEvent = string.Empty;
        public bool Optional;
    }

    internal sealed class PartRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public readonly List<string> States = new List<string>();
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> OptionalStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }

    internal sealed class BoltRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }

    internal sealed class CatalogRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public string? ScalarFloatName;
        public string? ScalarCommitState;
        public string? HostPayment;
        public readonly List<string> States = new List<string>();
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }
}
