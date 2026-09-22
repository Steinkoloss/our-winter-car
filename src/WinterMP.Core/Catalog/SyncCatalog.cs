using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using WinterMP.Core.Sync;
using WinterMP.Net;

namespace WinterMP.Core.Catalog
{
    /// <summary>
    /// Curated FSM descriptors (catalog/sync-catalog.json). Vehicle controls,
    /// house switches, ignitions, and starters are data-driven.
    /// </summary>
    public static class SyncCatalog
    {
        public const string FileName = "sync-catalog.json";

        public static bool Loaded { get; private set; }
        public static uint Hash { get; private set; }

        /// <summary>Loads the catalog on first use (session connect / handshake), not at plugin Awake.</summary>
        public static void EnsureLoaded()
        {
            if (Loaded) return;
            Load();
        }

        public static int ControlCount => _controls.Count;
        public static int SwitchRuleCount => _switchRules.Count;
        public static int BuyRuleCount => _buys.Count;
        public static int PartRuleCount => _parts.Count;
        public static int BoltRuleCount => _bolts.Count;

        private static readonly CatalogRuleSet _doors = new CatalogRuleSet();
        private static readonly CatalogRuleSet _spawnContainers = new CatalogRuleSet();
        private static readonly CatalogRuleSet _controls = new CatalogRuleSet();
        private static readonly CatalogRuleSet _switchRules = new CatalogRuleSet();
        private static readonly CatalogRuleSet _ignitions = new CatalogRuleSet();
        private static readonly CatalogRuleSet _starters = new CatalogRuleSet();
        private static readonly List<BuyCatalogRule> _buys = new List<BuyCatalogRule>();
        private static readonly List<PartCatalogRule> _parts = new List<PartCatalogRule>();
        private static readonly List<BoltCatalogRule> _bolts = new List<BoltCatalogRule>();
        private static VehicleRegistrationConfig _vehicles = new VehicleRegistrationConfig();
        private static PickableRegistrationConfig _pickables = new PickableRegistrationConfig();
        private static ConsumableConfig _consumables = new ConsumableConfig();
        private static VehicleClimateConfig _vehicleClimate = new VehicleClimateConfig();
        internal static PassengerCondensationData? PassengerCondensation { get; private set; }
        internal static PassengerHeatingData? PassengerHeating { get; private set; }
        internal static string? PassengerHeatingError { get; private set; }
        internal static BankingData? Banking { get; private set; }
        internal static VehicleDamageData? VehicleDamage { get; private set; }
        internal static GuestEngineProtectionData? GuestEngineProtection { get; private set; }
        internal static string? GuestEngineProtectionError { get; private set; }
        internal static VehicleElectricalData? VehicleElectrical { get; private set; }
        internal static string? VehicleElectricalError { get; private set; }
        internal static VehicleCoolingData? VehicleCooling { get; private set; }
        internal static string? VehicleCoolingError { get; private set; }
        internal static VehicleDrivetrainWearData? VehicleDrivetrainWear { get; private set; }
        internal static string? VehicleDrivetrainWearError { get; private set; }
        internal static VehicleDifferentialSpeedData? VehicleDifferentialSpeed { get; private set; }
        internal static string? VehicleDifferentialSpeedError { get; private set; }
        internal static VehicleHeatData? VehicleHeat { get; private set; }
        internal static string? VehicleHeatError { get; private set; }
        internal static VehicleWearInputData? VehicleWearInputs { get; private set; }
        internal static string? VehicleWearInputsError { get; private set; }
        internal static VehicleTemperatureData? VehicleTemperature { get; private set; }
        internal static string? VehicleTemperatureError { get; private set; }
        internal static VehicleTirePressureData? VehicleTirePressure { get; private set; }
        internal static VehicleWheelHealthData? VehicleWheelHealth { get; private set; }
        internal static string? VehicleTirePressureError { get; private set; }
        internal static string? VehicleWheelHealthError { get; private set; }
        internal static VehicleEngineRpmData? VehicleEngineRpm { get; private set; }
        internal static ParkingJointData? ParkingJoint { get; private set; }
        internal static AtfRefillData? AtfRefill { get; private set; }
        internal static MotorOilData? MotorOil { get; private set; }
        internal static AdvertPhoneData? AdvertPhone { get; private set; }
        internal static AdvertsData? Adverts { get; private set; }
        internal static TrainData? Train { get; private set; }
        internal static CoffeeData? Coffee { get; private set; }
        internal static VendorCoffeeData? VendorCoffee { get; private set; }
        internal static TaxiPassengersData? TaxiPassengers { get; private set; }
        internal static SausagesData? Sausages { get; private set; }
        internal static TractorTrailerData? TractorTrailer { get; private set; }
        internal static HouseholdFuseData? HouseholdFuses { get; private set; }
        internal static StoveData? Stoves { get; private set; }
        internal static ParkingBrakeData? ParkingBrake { get; private set; }
        internal static VehicleEngineHandoffData? VehicleEngineHandoff { get; private set; }
        internal static string? VehicleEngineRpmError { get; private set; }
        internal static GuestEngineInputsData? GuestEngineInputs { get; private set; }
        internal static string? GuestEngineInputsError { get; private set; }
        internal static SlotMachineData? SlotMachines { get; private set; }
        internal static PokerData? VideoPoker { get; private set; }
        internal static DebtLetterData? DebtLetter { get; private set; }
        internal static VenttiPropertyData? VenttiProperty { get; private set; }
        internal static VenttiTableData? VenttiTable { get; private set; }
        internal static RallyProgressData? RallyProgress { get; private set; }
        internal static LottoDrawData? LottoDraw { get; private set; }
        internal static HockeyBettingData? HockeyBetting { get; private set; }
        internal static LottoTicketsData? LottoTickets { get; private set; }
        internal static TrophyFactoriesData? TrophyFactories { get; private set; }
        internal static PartsPackagesData? PartsPackages { get; private set; }
        internal static ShoppingBagsData? ShoppingBags { get; private set; }
        internal static PaneScrapeData? PaneScrape { get; private set; }
        internal static WoodstoveFuelData? WoodstoveFuel { get; private set; }
        internal static PartIdentityData? PartIdentity { get; private set; }
        internal static ReplacementPartsData? ReplacementParts { get; private set; }
        internal static ValveAdjustmentData? ValveAdjustment { get; private set; }
        internal static CylinderHeadData? CylinderHead { get; private set; }
        internal static FirewoodDeliveryData? FirewoodDelivery { get; private set; }
        internal static TaxiFareData? TaxiFare { get; private set; }
        internal static TaxiMeterData? TaxiMeter { get; private set; }
        internal static TaxiServiceData? TaxiService { get; private set; }
        internal static TaxiPickupData? TaxiPickup { get; private set; }
        internal static List<FirewoodBuyerData>? FirewoodBuyers { get; private set; }
        internal static FleaSaleData? FleaSale { get; private set; }
        internal static UtilityPaymentsData? UtilityPayments { get; private set; }
        internal static UtilityPaymentsData? PhonePayments { get; private set; }
        internal static MooseChopData? MooseChop { get; private set; }
        internal static MooseMeatData? MooseMeat { get; private set; }
        internal static MilkConditionData? MilkCondition { get; private set; }

        public static void Load()
        {
            _doors.Clear();
            _spawnContainers.Clear();
            TrophyFactories = null;
            PartsPackages = null;
            ShoppingBags = null;
            PaneScrape = null;
            WoodstoveFuel = null;
            PartIdentity = null;
            ReplacementParts = null;
            FirewoodBuyers = null; FirewoodDelivery = null;
            TaxiFare = null;
            TaxiMeter = null;
            TaxiService = null;
            TaxiPickup = null;
            FleaSale = null;
            ValveAdjustment = null; CylinderHead = null; MilkCondition = null; MooseMeat = null; MooseChop = null; UtilityPayments = null; PhonePayments = null;
            _controls.Clear();
            _switchRules.Clear();
            _ignitions.Clear();
            _starters.Clear();
            _buys.Clear();
            _parts.Clear();
            _bolts.Clear();
            _vehicles = new VehicleRegistrationConfig();
            _pickables = new PickableRegistrationConfig();
            _consumables = new ConsumableConfig();
            _vehicleClimate = new VehicleClimateConfig();
            PassengerCondensation = null;
            PassengerHeating = null;
            PassengerHeatingError = null;
            Banking = null;
            VehicleDamage = null;
            GuestEngineProtection = null;
            GuestEngineProtectionError = null;
            VehicleElectrical = null;
            VehicleElectricalError = null;
            VehicleCooling = null;
            VehicleCoolingError = null;
            VehicleDrivetrainWear = null;
            VehicleDrivetrainWearError = null;
            VehicleDifferentialSpeed = null;
            VehicleDifferentialSpeedError = null;
            VehicleHeat = null;
            VehicleHeatError = null;
            VehicleWearInputs = null;
            VehicleWearInputsError = null;
            VehicleTemperature = null;
            VehicleTemperatureError = null;
            VehicleTirePressure = null;
            VehicleWheelHealth = null;
            VehicleTirePressureError = null;
            VehicleWheelHealthError = null;
            VehicleEngineRpm = null;
            VehicleEngineHandoff = null;
            ParkingBrake = null; ParkingJoint = null; Stoves = null; HouseholdFuses = null; TractorTrailer = null; Sausages = null; TaxiPassengers = null; Coffee = null; VendorCoffee = null; Train = null; Adverts = null; AdvertPhone = null; MotorOil = null; AtfRefill = null;
            VehicleEngineRpmError = null;
            GuestEngineInputs = null;
            GuestEngineInputsError = null;
            SlotMachines = null;
            VideoPoker = null;
            DebtLetter = null;
            VenttiProperty = null;
            VenttiTable = null;
            RallyProgress = null;
            LottoDraw = null;
            HockeyBetting = null;
            LottoTickets = null;
            Loaded = false;
            Hash = 0;

            string path = Path.Combine(GetPluginDirectory(), FileName);
            if (!File.Exists(path))
            {
                WinterMPPlugin.Log.LogError(
                    $"SyncCatalog: missing '{path}'. Vehicle/house FSM rules will not sync.");
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"SyncCatalog: could not read '{path}': {e.Message}");
                return;
            }

            Hash = StableHash.Fnv1a32(json);

            SyncCatalogData data;
            try
            {
                data = SyncCatalogJson.Parse(json);
                LoadRules(data.Doors, _doors);
                LoadRules(data.SpawnContainers, _spawnContainers);
                LoadRules(data.Controls, _controls);
                LoadRules(data.SwitchRules, _switchRules);
                LoadRules(data.Ignitions, _ignitions);
                LoadRules(data.Starters, _starters);
                LoadBuyRules(data.Buys, _buys);
                LoadPartRules(data.Parts, _parts);
                LoadBoltRules(data.Bolts, _bolts);
                ApplyVehicleConfig(data);
                Banking = data.Banking;
                VehicleDamage = data.VehicleDamage;
                GuestEngineProtection = data.GuestEngineProtection;
                GuestEngineProtectionError = data.GuestEngineProtectionError;
                VehicleElectrical = data.VehicleElectrical;
                VehicleElectricalError = data.VehicleElectricalError;
                VehicleCooling = data.VehicleCooling;
                VehicleCoolingError = data.VehicleCoolingError;
                VehicleDrivetrainWear = data.VehicleDrivetrainWear;
                VehicleDrivetrainWearError = data.VehicleDrivetrainWearError;
                VehicleDifferentialSpeed = data.VehicleDifferentialSpeed;
                VehicleDifferentialSpeedError = data.VehicleDifferentialSpeedError;
                VehicleHeat = data.VehicleHeat;
                VehicleHeatError = data.VehicleHeatError;
                VehicleWearInputs = data.VehicleWearInputs;
                VehicleWearInputsError = data.VehicleWearInputsError;
                VehicleTemperature = data.VehicleTemperature;
                VehicleTemperatureError = data.VehicleTemperatureError;
                VehicleTirePressure = data.VehicleTirePressure;
                VehicleWheelHealth = data.VehicleWheelHealth;
                VehicleTirePressureError = data.VehicleTirePressureError;
                VehicleWheelHealthError = data.VehicleWheelHealthError;
                VehicleEngineRpm = data.VehicleEngineRpm;
                ParkingBrake = data.ParkingBrake;
                ParkingJoint = data.ParkingJoint;
                AtfRefill = data.AtfRefill;
                if (data.AtfRefillError != null) WinterMPPlugin.Log.LogError("ATF refill metadata unavailable: " + data.AtfRefillError);
                MotorOil = data.MotorOil;
                if (data.MotorOilError != null) WinterMPPlugin.Log.LogWarning("Motor oil unavailable: " + data.MotorOilError);
                AdvertPhone = data.AdvertPhone;
                if (data.AdvertPhoneError != null) WinterMPPlugin.Log.LogWarning("Advert phone unavailable: " + data.AdvertPhoneError);
                Adverts = data.Adverts;
                if (data.AdvertsError != null) WinterMPPlugin.Log.LogWarning("Adverts unavailable: " + data.AdvertsError);
                Train = data.Train;
                if (data.TrainError != null) WinterMPPlugin.Log.LogWarning("Train unavailable: " + data.TrainError);
                Coffee = data.Coffee;
                if (data.CoffeeError != null) WinterMPPlugin.Log.LogWarning("Home coffee unavailable: " + data.CoffeeError);
                VendorCoffee = data.VendorCoffee;
                WinterMPPlugin.Log.LogWarning("Vendor coffee disabled: " + (data.VendorCoffeeError ?? VendorCoffee?.DisabledReason ?? "vendorCoffee catalog missing"));
                TaxiPassengers = data.TaxiPassengers;
                if (data.TaxiPassengersError != null) WinterMPPlugin.Log.LogWarning("Taxi passengers unavailable: " + data.TaxiPassengersError);
                Sausages = data.Sausages;
                if (data.SausagesError != null) WinterMPPlugin.Log.LogWarning("Sausages unavailable: " + data.SausagesError);
                TractorTrailer = data.TractorTrailer;
                if (data.TractorTrailerError != null) WinterMPPlugin.Log.LogWarning("Tractor trailer unavailable: " + data.TractorTrailerError);
                HouseholdFuses = data.HouseholdFuses;
                if (data.HouseholdFusesError != null) WinterMPPlugin.Log.LogWarning("Household fuses unavailable: " + data.HouseholdFusesError);
                Stoves = data.Stoves;
                if (data.StovesError != null) WinterMPPlugin.Log.LogError("Stove metadata unavailable: " + data.StovesError);
                if (data.ParkingJointError != null) WinterMPPlugin.Log.LogError("Parking joint metadata unavailable: " + data.ParkingJointError);
                if (data.ParkingBrakeError != null) WinterMPPlugin.Log.LogError("Parking brake sync unavailable: " + data.ParkingBrakeError);
                VehicleEngineHandoff = data.VehicleEngineHandoff;
                if (data.VehicleEngineHandoffError != null)
                    WinterMPPlugin.Log.LogError("SyncCatalog: engine handoff unavailable: " + data.VehicleEngineHandoffError);
                VehicleEngineRpmError = data.VehicleEngineRpmError;
                GuestEngineInputs = data.GuestEngineInputs;
                GuestEngineInputsError = data.GuestEngineInputsError;
                SlotMachines = data.SlotMachines;
                VideoPoker = data.VideoPoker;
                DebtLetter = data.DebtLetter;
                VenttiProperty = data.VenttiProperty;
                VenttiTable = data.VenttiTable;
                RallyProgress = data.RallyProgress;
                LottoDraw = data.LottoDraw;
                HockeyBetting = data.HockeyBetting;
                LottoTickets = data.LottoTickets;
                TrophyFactories = data.TrophyFactories;
                PartsPackages = data.PartsPackages;
                ShoppingBags = data.ShoppingBags;
                PaneScrape = data.PaneScrape;
                WoodstoveFuel = data.WoodstoveFuel;
                PartIdentity = data.PartIdentity;
                ReplacementParts = data.ReplacementParts;
                FirewoodDelivery = data.FirewoodDelivery;
                TaxiFare = data.TaxiFare;
                if (data.TaxiFareError != null) WinterMPPlugin.Log.LogWarning("Shared taxi fare disabled: " + data.TaxiFareError);
                TaxiMeter = data.TaxiMeter;
                if (data.TaxiMeterError != null) WinterMPPlugin.Log.LogWarning("Shared taxi meter disabled: " + data.TaxiMeterError);
                TaxiService = data.TaxiService;
                if (data.TaxiServiceError != null) WinterMPPlugin.Log.LogWarning("Shared taxi service disabled: " + data.TaxiServiceError);
                TaxiPickup = data.TaxiPickup;
                if (data.TaxiPickupError != null) WinterMPPlugin.Log.LogWarning("Guest taxi pickup disabled: " + data.TaxiPickupError);
                if (data.FirewoodDeliveryError != null) WinterMPPlugin.Log.LogWarning("Firewood delivery sync disabled: " + data.FirewoodDeliveryError);
                FirewoodBuyers = data.FirewoodBuyers;
                if (data.FirewoodBuyersError != null) WinterMPPlugin.Log.LogWarning("Firewood buyer sync disabled: " + data.FirewoodBuyersError);
                ValveAdjustment = data.ValveAdjustment; CylinderHead = data.CylinderHead; MilkCondition = data.MilkCondition;
                FleaSale = data.FleaSale;
                if (data.FleaSaleError != null) WinterMPPlugin.Log.LogError("Flea sale metadata unavailable: " + data.FleaSaleError);
                UtilityPayments = data.UtilityPayments;
                PhonePayments = data.PhonePayments;
                if (data.PhonePaymentsError != null) WinterMPPlugin.Log.LogWarning("Phone payments disabled: " + data.PhonePaymentsError);
                if (data.UtilityPaymentsError != null) WinterMPPlugin.Log.LogWarning("Electricity payments disabled: " + data.UtilityPaymentsError);
                MooseChop = data.MooseChop;
                if (data.MooseChopError != null) WinterMPPlugin.Log.LogWarning("Moose chopping disabled: " + data.MooseChopError);
                MooseMeat = data.MooseMeat;
                if (data.MooseMeatError != null) WinterMPPlugin.Log.LogWarning("Moose meat sync disabled: " + data.MooseMeatError);
                if (data.MilkConditionError != null) WinterMPPlugin.Log.LogWarning("Milk condition sync disabled: " + data.MilkConditionError);
                if (data.CylinderHeadError != null) WinterMPPlugin.Log.LogWarning("Cylinder head attachment disabled: " + data.CylinderHeadError);
                if (data.ValveAdjustmentError != null) WinterMPPlugin.Log.LogWarning("Valve adjustment disabled: " + data.ValveAdjustmentError);
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogError($"SyncCatalog: invalid JSON in '{path}': {e.Message}");
                return;
            }

            Loaded = true;
            if (GuestEngineProtectionError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: guest engine protection unavailable; saved-part isolation must wait: " + GuestEngineProtectionError);
            if (VehicleElectricalError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: vehicle electrical RPM unavailable: " + VehicleElectricalError);
            if (VehicleCoolingError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: vehicle cooling speed unavailable: " + VehicleCoolingError);
            if (VehicleDrivetrainWearError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: drivetrain wear unavailable: " + VehicleDrivetrainWearError);
            if (VehicleDifferentialSpeedError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: differential speed unavailable: " + VehicleDifferentialSpeedError);
            if (VehicleHeatError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: vehicle heat inputs unavailable: " + VehicleHeatError);
            if (VehicleWearInputsError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: vehicle wear inputs unavailable: " + VehicleWearInputsError);
            if (VehicleTemperatureError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: vehicle temperature source unavailable: " + VehicleTemperatureError);
            if (VehicleTirePressureError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: tyre pressure application unavailable: " + VehicleTirePressureError);
            if (VehicleWheelHealthError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: wheel health input unavailable: " + VehicleWheelHealthError);
            if (VehicleEngineRpmError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: vehicle engine RPM source unavailable: " + VehicleEngineRpmError);
            if (GuestEngineInputsError != null)
                WinterMPPlugin.Log.LogError("SyncCatalog: guest engine input projection unavailable: " + GuestEngineInputsError);
            string build = data.GameBuild ?? "?";
            WinterMPPlugin.Log.LogInfo(
                $"SyncCatalog: loaded {_doors.Count} doors, {_spawnContainers.Count} spawn-containers, {_controls.Count} controls, " +
                $"{_switchRules.Count} switch rules, {_ignitions.Count} ignitions, " +
                $"{_starters.Count} starters, {_buys.Count} buys, {_parts.Count} parts, {_bolts.Count} bolts " +
                $"(build '{build}', hash {Hash:X8}).");
        }

        public static string[]? TryMatchDoor(PlayMakerFSM fsm) => TryMatch(_doors, fsm);

        /// <summary>Use-FSM containers (grocery bags) whose "Spawn one/all" states pour
        /// out instantiated products. We replay the spawn state-enter so both peers spill;
        /// unlike doors this is a one-shot action, so it never joins the snapshot/checksum.</summary>
        public static string[]? TryMatchSpawnContainer(PlayMakerFSM fsm) => TryMatch(_spawnContainers, fsm);

        public static CatalogControlMatch? TryMatchControl(PlayMakerFSM fsm)
        {
            if (!Loaded || _controls.Count == 0) return null;

            string? scenePath = null, objectName = null;
            string fsmName = fsm.FsmName;
            var candidates = _controls.ForName(fsmName);
            if (candidates == null) return null;
            foreach (var rule in candidates)
            {
                if (!rule.Matches(scenePath ?? (scenePath = ScenePath.Of(fsm.transform)),
                        objectName ?? (objectName = fsm.gameObject.name), fsmName, fsm)) continue;
                return new CatalogControlMatch(rule.States, rule.ScalarFloatName, rule.ScalarCommitState, rule.HostPayment);
            }

            return null;
        }

        public static string[]? TryMatchSwitch(PlayMakerFSM fsm) => TryMatch(_switchRules, fsm);

        public static string[]? TryMatchIgnition(PlayMakerFSM fsm) => TryMatch(_ignitions, fsm);

        public static string[]? TryMatchStarter(PlayMakerFSM fsm) => TryMatch(_starters, fsm);

        internal static bool HasStarterOrControlRules(string fsmName) => Loaded
            && (_starters.ForName(fsmName) != null || _controls.ForName(fsmName) != null);

        /// <summary>Host-authoritative purchase / payment pipelines from catalog buys.</summary>
        public static bool TryMatchBuy(PlayMakerFSM fsm, out CatalogBuyMatch? match)
        {
            match = null;
            if (!Loaded || _buys.Count == 0) return false;

            foreach (var rule in _buys)
            {
                if (!rule.Matches(fsm)) continue;
                if (!rule.TryBuildMatch(fsm, out match)) continue;
                return true;
            }

            return false;
        }

        /// <summary>Car-part assembly Data FSMs (bolt on/off, install, remove).</summary>
        public static string[]? TryMatchPart(PlayMakerFSM fsm)
        {
            if (!Loaded || _parts.Count == 0) return null;

            foreach (var rule in _parts)
            {
                if (!rule.Matches(fsm)) continue;
                if (rule.TryBuildStates(fsm, out string[]? states)) return states;
            }

            return null;
        }

        /// <summary>Wrench Screw FSMs (tighten / untighten).</summary>
        public static bool TryMatchBolt(PlayMakerFSM fsm)
        {
            if (!Loaded || _bolts.Count == 0) return false;

            foreach (var rule in _bolts)
            {
                if (rule.Matches(fsm)) return true;
            }

            return false;
        }

        public static bool IsVehicleRoot(Rigidbody body) => _vehicles.IsVehicleRoot(body);

        public static bool IsPickableRigidbody(Rigidbody body) => _pickables.IsPickable(body);

        /// <summary>Pooled-instance name suffixes ("(itemx)", ...) — spawn template matching strips these.</summary>
        public static string[] PickableNameSuffixes => _pickables.NameSuffixes;

        public static void CollectConsumableDespawnStates(PlayMakerFSM fsm, List<string> states)
            => _consumables.CollectDespawnStates(fsm, states);

        public static bool IsClimateVehicleFsmPath(string path) => _vehicleClimate.IsClimateVehiclePath(path);

        public static bool IsCarTempFsmPath(string path) => _vehicleClimate.IsCarTempRoot(path);

        public static bool IsHeaterFsmPath(string path) => _vehicleClimate.IsHeaterPath(path);

        private static void ApplyVehicleConfig(SyncCatalogData data)
        {
            if (data.Vehicles != null)
            {
                _vehicles.MinMass = data.Vehicles.MinMass;
                _vehicles.RequireRoot = data.Vehicles.RequireRoot;
                if (data.Vehicles.NamePrefixes.Count > 0)
                    _vehicles.NamePrefixes = data.Vehicles.NamePrefixes.ToArray();
            }

            if (data.Pickables != null)
            {
                _pickables.ProbeUseFsm = data.Pickables.ProbeUseFsm;
                if (data.Pickables.ExcludeNameContains.Count > 0)
                    _pickables.ExcludeNameContains = data.Pickables.ExcludeNameContains.ToArray();
                if (data.Pickables.NameSuffixes.Count > 0)
                    _pickables.NameSuffixes = data.Pickables.NameSuffixes.ToArray();
            }

            if (data.Consumables != null)
            {
                if (data.Consumables.FsmName.Length > 0)
                    _consumables.FsmName = data.Consumables.FsmName;
                if (data.Consumables.DrinkCheckState.Length > 0)
                    _consumables.DrinkCheckState = data.Consumables.DrinkCheckState;
                if (data.Consumables.DestroyStates.Count > 0)
                    _consumables.DestroyStates = data.Consumables.DestroyStates.ToArray();
                if (data.Consumables.DrinkEmptyStates.Count > 0)
                    _consumables.DrinkEmptyStates = data.Consumables.DrinkEmptyStates.ToArray();
            }

            if (data.VehicleClimate != null)
            {
                PassengerCondensation = data.VehicleClimate.PassengerCondensation;
                PassengerHeating = data.VehicleClimate.PassengerHeating;
                PassengerHeatingError = data.VehicleClimate.PassengerHeatingError;
                if (data.VehicleClimate.PathPrefixes.Count > 0)
                    _vehicleClimate.PathPrefixes = data.VehicleClimate.PathPrefixes.ToArray();
                if (data.VehicleClimate.CarTempPathContains.Count > 0)
                    _vehicleClimate.CarTempPathContains = data.VehicleClimate.CarTempPathContains.ToArray();
                if (data.VehicleClimate.HeaterPathContains.Count > 0)
                    _vehicleClimate.HeaterPathContains = data.VehicleClimate.HeaterPathContains.ToArray();
            }
        }

        private static void LoadRules(List<CatalogRuleData> source, CatalogRuleSet target)
        {
            foreach (var rule in source)
            {
                target.Add(new CatalogRule(
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.States.ToArray(),
                    rule.ScalarFloatName,
                    rule.ScalarCommitState,
                    rule.HostPayment,
                    rule.RequireStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray()));
            }
        }

        private static void LoadBuyRules(List<BuyRuleData> source, List<BuyCatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new BuyCatalogRule(
                    rule.Template,
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.RequireStates.ToArray(),
                    rule.ResultStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray(),
                    rule.EntryGuards.ToArray()));
            }
        }

        private static void LoadPartRules(List<PartRuleData> source, List<PartCatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new PartCatalogRule(
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.States.ToArray(),
                    rule.RequireStates.ToArray(),
                    rule.OptionalStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray()));
            }
        }

        private static void LoadBoltRules(List<BoltRuleData> source, List<BoltCatalogRule> target)
        {
            foreach (var rule in source)
            {
                target.Add(new BoltCatalogRule(
                    rule.PathPrefix,
                    rule.PathContains,
                    rule.ObjectName,
                    rule.ObjectNameContains,
                    rule.FsmName,
                    rule.RequireStates.ToArray(),
                    rule.ExcludePathPrefixes.ToArray()));
            }
        }

        private static string[]? TryMatch(CatalogRuleSet rules, PlayMakerFSM fsm)
        {
            if (!Loaded || rules.Count == 0) return null;

            string? scenePath = null, objectName = null;
            string fsmName = fsm.FsmName;

            var candidates = rules.ForName(fsmName);
            if (candidates == null) return null;
            foreach (var rule in candidates)
            {
                if (!rule.Matches(scenePath ?? (scenePath = ScenePath.Of(fsm.transform)),
                        objectName ?? (objectName = fsm.gameObject.name), fsmName, fsm)) continue;
                return rule.States;
            }

            return null;
        }

        private static string GetPluginDirectory()
        {
            string? location = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(location))
                return ".";
            return Path.GetDirectoryName(location) ?? ".";
        }

        private sealed class CatalogRuleSet
        {
            private readonly Dictionary<string, List<CatalogRule>> _byName = new Dictionary<string, List<CatalogRule>>(StringComparer.Ordinal);
            internal int Count { get; private set; }
            internal void Add(CatalogRule rule)
            {
                if (!_byName.TryGetValue(rule.FsmName, out var group)) _byName.Add(rule.FsmName, group = new List<CatalogRule>());
                group.Add(rule);
                Count++;
            }
            internal void Clear() { _byName.Clear(); Count = 0; }
            internal List<CatalogRule>? ForName(string name) => _byName.TryGetValue(name, out var group) ? group : null;
        }

        private sealed class CatalogRule
        {
            private readonly string _pathPrefix;
            private readonly string? _pathContains;
            private readonly string? _objectName;
            private readonly string? _objectNameContains;
            private readonly string _fsmName;
            private readonly string[] _requireStates;
            private readonly string[] _excludePathPrefixes;

            internal readonly string[] States;
            internal readonly string? ScalarFloatName;
            internal readonly string? ScalarCommitState;
            internal readonly string? HostPayment;

            internal CatalogRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] states,
                string? scalarFloatName,
                string? scalarCommitState,
                string? hostPayment,
                string[] requireStates,
                string[] excludePathPrefixes)
            {
                _pathPrefix = pathPrefix;
                _pathContains = pathContains;
                _objectName = objectName;
                _objectNameContains = objectNameContains;
                _fsmName = fsmName;
                States = states;
                ScalarFloatName = scalarFloatName;
                ScalarCommitState = scalarCommitState;
                HostPayment = hostPayment;
                _requireStates = requireStates;
                _excludePathPrefixes = excludePathPrefixes;
            }

            internal string FsmName => _fsmName;

            internal bool Matches(string scenePath, string objectName, string fsmName, PlayMakerFSM fsm)
            {
                if (fsmName != _fsmName) return false;

                foreach (string excluded in _excludePathPrefixes)
                {
                    if (scenePath.StartsWith(excluded, StringComparison.Ordinal))
                        return false;
                }

                if (_pathPrefix.Length > 0 && !scenePath.StartsWith(_pathPrefix, StringComparison.Ordinal))
                    return false;

                if (_pathContains != null && scenePath.IndexOf(_pathContains, StringComparison.Ordinal) < 0)
                    return false;

                if (_objectName != null && objectName != _objectName)
                    return false;

                if (_objectNameContains != null
                    && objectName.IndexOf(_objectNameContains, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }

                foreach (string state in States)
                {
                    if (!FsmHook.HasState(fsm, state)) return false;
                }

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                return States.Length > 0;
            }
        }

        private sealed class BuyCatalogRule
        {
            private readonly string _template;
            private readonly CatalogPathMatch _path;
            private readonly string[] _requireStates;
            private readonly string[] _resultStates;
            private readonly BuyGuardData[] _entryGuards;

            internal BuyCatalogRule(
                string template,
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] requireStates,
                string[] resultStates,
                string[] excludePathPrefixes,
                BuyGuardData[] entryGuards)
            {
                _template = template;
                _path = new CatalogPathMatch(
                    pathPrefix, pathContains, objectName, objectNameContains, fsmName, excludePathPrefixes);
                _requireStates = requireStates;
                _resultStates = resultStates;
                _entryGuards = entryGuards;
            }

            internal bool Matches(PlayMakerFSM fsm) => _path.Matches(fsm);

            internal bool TryBuildMatch(PlayMakerFSM fsm, out CatalogBuyMatch? match)
            {
                if (_template == "shopBuy")
                    return ShopBuyInference.TryInfer(fsm, out match);

                match = null;

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                var guards = new List<CatalogBuyGuard>(_entryGuards.Length);
                foreach (var guard in _entryGuards)
                {
                    bool exists = FsmHook.HasState(fsm, guard.StateName);
                    if (!exists)
                    {
                        if (guard.Optional) continue;
                        return false;
                    }

                    guards.Add(new CatalogBuyGuard
                    {
                        StateName = guard.StateName,
                        TriggerEvent = guard.TriggerEvent,
                    });
                }

                if (guards.Count == 0) return false;

                var results = new List<string>(_resultStates.Length);
                foreach (string state in _resultStates)
                {
                    if (FsmHook.HasState(fsm, state)) results.Add(state);
                }

                if (results.Count == 0) return false;

                match = new CatalogBuyMatch(guards.ToArray(), results.ToArray());
                return true;
            }
        }

        private sealed class PartCatalogRule
        {
            private readonly CatalogPathMatch _path;
            private readonly string[] _states;
            private readonly string[] _requireStates;
            private readonly string[] _optionalStates;

            internal PartCatalogRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] states,
                string[] requireStates,
                string[] optionalStates,
                string[] excludePathPrefixes)
            {
                _path = new CatalogPathMatch(
                    pathPrefix, pathContains, objectName, objectNameContains, fsmName, excludePathPrefixes);
                _states = states;
                _requireStates = requireStates;
                _optionalStates = optionalStates;
            }

            internal bool Matches(PlayMakerFSM fsm) => _path.Matches(fsm);

            internal bool TryBuildStates(PlayMakerFSM fsm, out string[]? states)
            {
                states = null;

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                foreach (string state in _states)
                {
                    if (!FsmHook.HasState(fsm, state)) return false;
                }

                var synced = new List<string>(_states.Length + _optionalStates.Length);
                foreach (string state in _states)
                    synced.Add(state);

                foreach (string state in _optionalStates)
                {
                    if (FsmHook.HasState(fsm, state)) synced.Add(state);
                }

                if (synced.Count == 0) return false;

                states = synced.ToArray();
                return true;
            }
        }

        private sealed class BoltCatalogRule
        {
            private readonly CatalogPathMatch _path;
            private readonly string[] _requireStates;

            internal BoltCatalogRule(
                string pathPrefix,
                string? pathContains,
                string? objectName,
                string? objectNameContains,
                string fsmName,
                string[] requireStates,
                string[] excludePathPrefixes)
            {
                _path = new CatalogPathMatch(
                    pathPrefix, pathContains, objectName, objectNameContains, fsmName, excludePathPrefixes);
                _requireStates = requireStates;
            }

            internal bool Matches(PlayMakerFSM fsm)
            {
                if (!_path.Matches(fsm)) return false;

                foreach (string required in _requireStates)
                {
                    if (!FsmHook.HasState(fsm, required)) return false;
                }

                return true;
            }
        }
    }

    /// <summary>Matched control states plus optional scalar commit metadata from the sync catalog.</summary>
    public sealed class CatalogControlMatch
    {
        public readonly string[] States;
        public readonly string? ScalarFloatName;
        public readonly string? ScalarCommitState;
        public readonly string? HostPayment;

        internal CatalogControlMatch(string[] states, string? scalarFloatName, string? scalarCommitState, string? hostPayment)
        {
            States = states;
            ScalarFloatName = scalarFloatName;
            ScalarCommitState = scalarCommitState;
            HostPayment = hostPayment;
        }
    }
}
