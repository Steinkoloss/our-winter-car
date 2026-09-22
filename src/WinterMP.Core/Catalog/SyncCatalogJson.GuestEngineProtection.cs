using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static GuestEngineProtectionData ParseGuestEngineProtection(object? value)
        {
            if (value is not Dictionary<string, object?> obj)
                throw new FormatException("Invalid guest engine protection profile.");
            var data = new GuestEngineProtectionData();
            var identities = new HashSet<string>();
            foreach (var entry in EngineProtectionEntries(obj, "writers", 1, 32))
            {
                var writer = new GuestEngineWriterData { Path = EngineProtectionPath(entry),
                    Fsm = EngineProtectionName(entry, "fsm") };
                if (!identities.Add(writer.Path + "::" + writer.Fsm))
                    throw new FormatException("Duplicate guest engine protection FSM.");
                var actions = new HashSet<string>();
                foreach (var action in EngineProtectionEntries(entry, "actions", 1, 256))
                {
                    var binding = new GuestEngineWriteActionData {
                        State = EngineProtectionName(action, "state"),
                        Index = SlotNumber(action.TryGetValue("index", out var index) ? index : null, "engine action index", 0, 255),
                        ActionType = EngineProtectionName(action, "actionType"),
                        TargetVariable = EngineProtectionName(action, "targetVariable"),
                        TargetFsm = EngineProtectionName(action, "targetFsm"),
                        TargetScalar = EngineProtectionName(action, "targetScalar") };
                    if (binding.ActionType != "SetFsmFloat" && binding.ActionType != "AddFsmFloat"
                        && binding.ActionType != "SubtractFsmFloat" && binding.ActionType != "SetFsmInt")
                        throw new FormatException("Unsupported guest engine write action type.");
                    if (!actions.Add(binding.State + "::" + binding.Index))
                        throw new FormatException("Duplicate guest engine write action.");
                    if (action.TryGetValue("gearboxWear", out var gearboxWear))
                    {
                        if (gearboxWear is not bool observe || !observe || writer.Path != "CORRIS/Simulation/Systems/Drivetrain/GearboxDamage"
                            || writer.Fsm != "Damage" || binding.State != "Reverse" || binding.Index != 6
                            || binding.ActionType != "SubtractFsmFloat" || binding.TargetVariable != "db_Gearbox"
                            || binding.TargetFsm != "Data" || binding.TargetScalar != "Wear")
                            throw new FormatException("Unsupported gearbox failure-wear observation.");
                        binding.GearboxWear = true;
                    }
                    if (action.TryGetValue("gearboxOilUse", out var oilUse))
                    {
                        if (oilUse is not bool observe || !observe || writer.Path != "CORRIS/Simulation/Systems/Drivetrain/GearboxAutomatic"
                            || writer.Fsm != "3 speed" || binding.Index != 3 || (binding.State != "State 1" && binding.State != "State 3")
                            || binding.ActionType != "SubtractFsmFloat" || binding.TargetVariable != "db_Gearbox"
                            || binding.TargetFsm != "Data" || binding.TargetScalar != "OilLevel")
                            throw new FormatException("Unsupported gearbox oil-use observation.");
                        binding.GearboxOilUse = true;
                    }
                    if (action.TryGetValue("starterDraw", out var draw))
                    {
                        if (draw is not string kind || kind != "loaded" && kind != "unloaded")
                            throw new FormatException("Invalid starter draw kind.");
                        binding.StarterDraw = (byte)(kind == "loaded" ? 1 : 2);
                        string signature = binding.State + "#" + binding.Index;
                        bool loaded = signature == "Turn key#6" || signature == "Fuel Mixture#8"
                            || signature == "Start or not#7" || signature == "Start engine#9";
                        if (writer.Path != "CORRIS/Simulation/STARTERxCorris" || writer.Fsm != "Starter"
                            || binding.ActionType != "AddFsmFloat" || binding.TargetVariable != "db_Battery"
                            || binding.TargetFsm != "Data" || binding.TargetScalar != "Charge"
                            || (kind == "loaded" ? !loaded : signature != "No Flywheel#5"))
                            throw new FormatException("Unsupported starter draw observation.");
                    }
                    if (action.TryGetValue("starterWear", out var wear))
                    {
                        if (wear is not bool observe || !observe || binding.StarterDraw != 0
                            || writer.Path != "CORRIS/Simulation/STARTERxCorris" || writer.Fsm != "Starter"
                            || binding.State != "Fuel Mixture" || binding.Index != 11 || binding.ActionType != "SubtractFsmFloat"
                            || binding.TargetVariable != "db_Starter" || binding.TargetFsm != "Data" || binding.TargetScalar != "Wear")
                            throw new FormatException("Unsupported starter wear observation.");
                        binding.StarterWear = true;
                    }
                    writer.Actions.Add(binding);
                }
                ParseDrivetrainProtection(entry, writer, actions);
                if (entry.ContainsKey("poseActions"))
                    foreach (var pose in EngineProtectionEntries(entry, "poseActions", 1, 16))
                    {
                        var binding = new GuestEnginePoseActionData {
                            State = EngineProtectionName(pose, "state"),
                            Index = SlotNumber(pose.TryGetValue("index", out var index) ? index : null, "engine pose index", 0, 255),
                            TargetVariable = EngineProtectionName(pose, "targetVariable"),
                            AngleVariable = EngineProtectionName(pose, "angleVariable") };
                        if (!actions.Add(binding.State + "::" + binding.Index))
                            throw new FormatException("Duplicate guest engine pose or write action.");
                        writer.PoseActions.Add(binding);
                    }
                if (entry.TryGetValue("wheelHealth", out var wheelHealth))
                {
                    if (wheelHealth is not Dictionary<string, object?> health || writer.Fsm != "Condition")
                        throw new FormatException("Invalid wheel health reader profile.");
                    writer.WheelHealthIndex = SlotNumber(health.TryGetValue("wheel", out var wheel) ? wheel : null, "wheel health index", 0, 3);
                    var readStates = new HashSet<string>();
                    foreach (var read in EngineProtectionEntries(health, "reads", 2, 2))
                    {
                        var binding = new GuestWheelHealthReadData { State = EngineProtectionName(read, "state"),
                            Index = SlotNumber(read.TryGetValue("index", out var index) ? index : null, "wheel health reader index", 0, 255) };
                        if (!readStates.Add(binding.State)) throw new FormatException("Duplicate wheel health reader state.");
                        writer.WheelHealthReads.Add(binding);
                    }
                    bool wear = false, flat = false;
                    foreach (var write in writer.Actions)
                    {
                        if (write.TargetVariable != "ThisTire" || write.TargetFsm != "Data" || write.TargetScalar != "TireHealth") continue;
                        wear |= write.State == "State 1" && write.ActionType == "SubtractFsmFloat";
                        flat |= write.State == "Flat friction" && write.ActionType == "SetFsmFloat";
                    }
                    if (!wear || !flat || !readStates.Contains("State 1") || !readStates.Contains("Flat friction"))
                        throw new FormatException("Wheel health readers require both saved-tyre writer guards.");
                    foreach (var read in writer.WheelHealthReads)
                        if (actions.Contains(read.State + "::" + read.Index)) throw new FormatException("Wheel health reader overlaps a writer.");
                    if (health.TryGetValue("rim", out var rim)) writer.WheelRim = ParseWheelRim(rim);
                }
                if (entry.TryGetValue("gearboxCondition", out var gearboxCondition))
                {
                    if (gearboxCondition is not Dictionary<string, object?> read
                        || writer.Path != "CORRIS/Simulation/Systems/Drivetrain/GearboxDamage" || writer.Fsm != "Damage")
                        throw new FormatException("Invalid gearbox condition reader profile.");
                    var binding = new GuestGearboxConditionReadData { State = EngineProtectionName(read, "state"),
                        Index = SlotNumber(read.TryGetValue("index", out var index) ? index : null, "gearbox condition reader index", 0, 255) };
                    bool protectedWear = false;
                    foreach (var write in writer.Actions)
                        protectedWear |= write.State == "Reverse" && write.Index == 6 && write.ActionType == "SubtractFsmFloat"
                            && write.TargetVariable == "db_Gearbox" && write.TargetFsm == "Data" && write.TargetScalar == "Wear" && write.GearboxWear;
                    if (!protectedWear || binding.State != "Damage type" || binding.Index != 0
                        || actions.Contains(binding.State + "::" + binding.Index))
                        throw new FormatException("Gearbox condition reader requires its native saved-wear guard.");
                    writer.GearboxConditionRead = binding;
                }
                data.Writers.Add(writer);
            }
            foreach (var entry in EngineProtectionEntries(obj, "pausedFsms", 1, 32))
            {
                var paused = new GuestEnginePausedFsmData { Path = EngineProtectionPath(entry),
                    Fsm = EngineProtectionName(entry, "fsm") };
                if (!identities.Add(paused.Path + "::" + paused.Fsm))
                    throw new FormatException("Duplicate guest engine protection FSM.");
                var states = ReplacementStrings(entry, "requiredStates", 1, 128);
                foreach (string state in states) EngineProtectionName(state, "requiredStates");
                paused.RequiredStates = states;
                if (entry.TryGetValue("blockExternalFloatWrites", out var external))
                {
                    if (external is not bool block) throw new FormatException("Invalid external scalar write protection.");
                    paused.BlockExternalFloatWrites = block;
                }
                if (entry.TryGetValue("blockExternalIntWrites", out var integers))
                {
                    if (integers is not bool block) throw new FormatException("Invalid external integer write protection.");
                    paused.BlockExternalIntWrites = block;
                }
                if (entry.ContainsKey("rootPrefix") || entry.ContainsKey("relativePath"))
                {
                    paused.RootPrefix = EngineProtectionName(entry, "rootPrefix");
                    paused.RelativePath = EngineProtectionName(entry, "relativePath");
                    bool rockerCover = paused.RootPrefix == "VIN111" && paused.RelativePath == "VINP_RockerCover" && paused.Path == "CARPARTS/StartParts/VIN1110/VINP_RockerCover";
                    bool oilpan = paused.RootPrefix == "VIN101" && paused.RelativePath == "VINP_Oilpan" && paused.Path == "CARPARTS/StartParts/VIN1010/VINP_Oilpan";
                    bool head = paused.RootPrefix == "VIN101" && paused.RelativePath == "VINP_Cylinderhead"
                        && paused.Path == "CARPARTS/StartParts/VIN1010/VINP_Cylinderhead";
                    bool carburettor = paused.RootPrefix == "VIN111" && paused.RelativePath == "VINP_Carburettor"
                        && paused.Path == "CARPARTS/StartParts/VIN1110/VINP_Carburettor";
                    bool airCleaner = paused.RootPrefix == "VIN111" && paused.RelativePath == "VINP_AirCleaner"
                        && paused.Path == "CARPARTS/StartParts/VIN1110/VINP_AirCleaner";
                    bool headers = paused.RootPrefix == "VIN111" && paused.RelativePath == "VINP_ExhaustManifold"
                        && paused.Path == "CARPARTS/StartParts/VIN1110/VINP_ExhaustManifold";
                    if ((!rockerCover && !oilpan && !head && !carburettor && !airCleaner && !headers) || paused.Fsm != "Data")
                        throw new FormatException("Unsupported movable engine assembly protection.");
                }
                data.PausedFsms.Add(paused);
            }
            ValidateDrivetrainDestinations(data);
            return data;
        }

        private static IEnumerable<Dictionary<string, object?>> EngineProtectionEntries(
            Dictionary<string, object?> obj, string key, int min, int max)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> list || list.Count < min || list.Count > max)
                throw new FormatException("Invalid guest engine protection " + key + ".");
            foreach (var entry in list)
            {
                if (entry is not Dictionary<string, object?> fields)
                    throw new FormatException("Invalid guest engine protection " + key + " entry.");
                yield return fields;
            }
        }

        private static string EngineProtectionPath(Dictionary<string, object?> obj)
        {
            string path = RequiredString(obj, "path");
            if (path.Length > 512 || !ValidScenePath(path) || path.IndexOf(':') >= 0)
                throw new FormatException("Invalid guest engine protection path.");
            foreach (string part in path.Split('/'))
                if (part == "." || part.Trim().Length == 0)
                    throw new FormatException("Invalid guest engine protection path segment.");
            foreach (char ch in path)
                if (char.IsControl(ch)) throw new FormatException("Invalid guest engine protection path.");
            return path;
        }

        private static string EngineProtectionName(Dictionary<string, object?> obj, string key) =>
            EngineProtectionName(RequiredString(obj, key), key);

        private static string EngineProtectionName(string value, string key)
        {
            if (value.Trim().Length == 0 || value.Length > 128 || value.IndexOfAny(new[] { ':', '/', '\\' }) >= 0)
                throw new FormatException("Invalid guest engine protection " + key + ".");
            foreach (char ch in value)
                if (char.IsControl(ch)) throw new FormatException("Invalid guest engine protection " + key + ".");
            return value;
        }
    }

    internal sealed class GuestEngineProtectionData
    {
        public readonly List<GuestEngineWriterData> Writers = new List<GuestEngineWriterData>();
        public readonly List<GuestEnginePausedFsmData> PausedFsms = new List<GuestEnginePausedFsmData>();
    }

    internal sealed class GuestEngineWriterData
    {
        public string Path = "", Fsm = "";
        public readonly List<GuestEngineWriteActionData> Actions = new List<GuestEngineWriteActionData>();
        public readonly List<GuestEnginePoseActionData> PoseActions = new List<GuestEnginePoseActionData>();
        public readonly List<GuestEngineEventActionData> EventActions = new List<GuestEngineEventActionData>();
        public readonly List<GuestDrivetrainReadData> DrivetrainWearReads = new List<GuestDrivetrainReadData>();
        public GuestDrivetrainReadData? GearboxOilRead;
        public GuestGearboxConditionReadData? GearboxConditionRead;
        public int WheelHealthIndex = -1;
        public WheelRimData? WheelRim;
        public readonly List<GuestWheelHealthReadData> WheelHealthReads = new List<GuestWheelHealthReadData>();
    }

    internal sealed class GuestGearboxConditionReadData
    {
        public string State = string.Empty;
        public int Index;
    }

    internal sealed class GuestWheelHealthReadData
    {
        public string State = "";
        public int Index;
    }

    internal sealed class GuestEngineWriteActionData
    {
        public bool GearboxOilUse, GearboxWear;
        public string State = "", ActionType = "", TargetVariable = "", TargetFsm = "", TargetScalar = "";
        public int Index;
        public byte StarterDraw;
        public bool StarterWear;
    }

    internal sealed class GuestEnginePausedFsmData
    {
        public string Path = "", Fsm = "";
        public string RootPrefix = "", RelativePath = "";
        public string[] RequiredStates = new string[0];
        public bool BlockExternalFloatWrites;
        public bool BlockExternalIntWrites;
    }

    internal sealed class GuestEnginePoseActionData
    {
        public string State = "", TargetVariable = "", AngleVariable = "";
        public int Index;
    }
}
