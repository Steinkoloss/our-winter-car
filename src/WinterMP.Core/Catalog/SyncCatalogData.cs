using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
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
