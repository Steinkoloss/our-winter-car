using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleDrivetrainWearData ParseVehicleDrivetrainWear(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("targets", out var targets)
                || targets is not List<object?> rows || rows.Count != 3) throw new FormatException("Drivetrain wear requires three native targets.");
            var data = new VehicleDrivetrainWearData { RootPath = TemperaturePath(obj, "rootPath"), Path = TemperaturePath(obj, "path"), Fsm = TemperatureName(obj, "fsm") };
            if (!data.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal)) throw new FormatException("Foreign drivetrain wear graph.");
            var names = new HashSet<string>(); var paths = new HashSet<string>();
            foreach (var row in rows)
            {
                if (row is not Dictionary<string, object?> fields) throw new FormatException("Invalid drivetrain wear target.");
                var target = new VehicleDrivetrainWearTarget { Variable = TemperatureName(fields, "variable"),
                    Path = TemperaturePath(fields, "path"), Rate = TemperatureName(fields, "rate") };
                if (!fields.TryGetValue("divisor", out var number) || number is not long && number is not double)
                    throw new FormatException("Missing drivetrain wear divisor.");
                target.Divisor = number is double d ? (float)d : (long)number;
                if (float.IsNaN(target.Divisor) || float.IsInfinity(target.Divisor) || target.Divisor <= 0
                    || !target.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal)
                    || !names.Add(target.Variable) || !names.Add(target.Rate) || !paths.Add(target.Path))
                    throw new FormatException("Invalid or ambiguous drivetrain wear target.");
                data.Targets.Add(target);
            }
            return data;
        }

        private static VehicleDifferentialSpeedData ParseVehicleDifferentialSpeed(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid differential speed profile.");
            var data = new VehicleDifferentialSpeedData {
                RootPath = TemperaturePath(obj, "rootPath"), Path = TemperaturePath(obj, "path"),
                Fsm = TemperatureName(obj, "fsm"), State = TemperatureName(obj, "state"),
                ObjectVariable = TemperatureName(obj, "objectVariable"), ComponentType = TemperatureName(obj, "componentType"),
                Member = TemperatureName(obj, "member"), Output = TemperatureName(obj, "output"),
                WearState = TemperatureName(obj, "wearState"), WaitState = TemperatureName(obj, "waitState"),
                Index = SlotNumber(obj.TryGetValue("index", out var index) ? index : null, "differential speed reader index", 0, 255) };
            if (!data.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal) || data.State == data.WearState
                || data.State == data.WaitState || data.WearState == data.WaitState)
                throw new FormatException("Foreign or ambiguous differential speed source.");
            return data;
        }

        private static VehicleWearInputData ParseVehicleWearInputs(object? value)
        {
            if (value is not Dictionary<string, object?> obj || !obj.TryGetValue("readers", out var rows)
                || rows is not List<object?> list || list.Count != 7)
                throw new FormatException("Vehicle wear requires all seven native pressure/wear/oil inputs.");
            var data = new VehicleWearInputData { RootPath = TemperaturePath(obj, "rootPath"), Path = TemperaturePath(obj, "path") };
            if (!data.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal)) throw new FormatException("Foreign vehicle wear path.");
            var required = new HashSet<string> {
                "Pressure:Oil pressure:1:float1:RPM:Divide",
                "Wearing:State 1:7:float1:RPM:Divide", "Wearing:Calculate rate:1:float2:RPM:Multiply",
                "Wearing:Calculate rate 2:0:float2:RPM:Multiply", "Wearing:Oil level:2:float2:RPM:Multiply",
                "Wearing:Pressure leak:14:float1:RPM:Divide",
                "Oil:Oil contamination:2:float1:RPM:Divide" };
            foreach (var row in list)
            {
                if (row is not Dictionary<string, object?> fields) throw new FormatException("Invalid vehicle wear reader.");
                var reader = new VehicleWearReaderData {
                    Fsm = TemperatureName(fields, "fsm"), State = TemperatureName(fields, "state"),
                    Index = SlotNumber(fields.TryGetValue("index", out var index) ? index : null, "wear action index", 0, 255),
                    Field = TemperatureName(fields, "field"), Global = TemperatureName(fields, "global"),
                    Operation = TemperatureName(fields, "operation"), Output = TemperatureName(fields, "output") };
                if (!required.Remove(reader.Fsm + ":" + reader.State + ":" + reader.Index + ":" + reader.Field + ":" + reader.Global + ":" + reader.Operation)
                    || !fields.TryGetValue("everyFrame", out var frame) || frame is not bool everyFrame
                    || everyFrame != (reader.Fsm == "Pressure")) throw new FormatException("Changed or duplicated vehicle wear reader.");
                reader.EveryFrame = everyFrame;
                bool variable = fields.ContainsKey("otherVariable"), constant = fields.ContainsKey("otherConstant");
                if (variable == constant) throw new FormatException("Vehicle wear reader needs exactly one other operand.");
                if (variable) reader.OtherVariable = TemperatureName(fields, "otherVariable");
                else
                {
                    object? number = fields["otherConstant"];
                    if (number is not long && number is not double) throw new FormatException("Invalid vehicle wear constant.");
                    float scalar = number is double d ? (float)d : (long)number;
                    if (float.IsNaN(scalar) || float.IsInfinity(scalar) || (reader.Operation == "Divide" && scalar == 0))
                        throw new FormatException("Invalid vehicle wear constant.");
                    reader.OtherConstant = scalar;
                }
                data.Readers.Add(reader);
            }
            return data;
        }
    }
    internal sealed class VehicleWearInputData
    {
        public string RootPath = "", Path = "";
        public readonly List<VehicleWearReaderData> Readers = new List<VehicleWearReaderData>();
    }
    internal sealed class VehicleDifferentialSpeedData
    {
        public string RootPath = "", Path = "", Fsm = "", State = "", WearState = "", WaitState = "";
        public string ObjectVariable = "", ComponentType = "", Member = "", Output = "";
        public int Index;
    }
    internal sealed class VehicleDrivetrainWearData
    {
        public string RootPath = "", Path = "", Fsm = "";
        public readonly List<VehicleDrivetrainWearTarget> Targets = new List<VehicleDrivetrainWearTarget>();
    }
    internal sealed class VehicleDrivetrainWearTarget
    {
        public string Variable = "", Path = "", Rate = "";
        public float Divisor;
    }
    internal sealed class VehicleWearReaderData
    {
        public string Fsm = "", State = "", Field = "", Global = "", Operation = "", Output = "";
        public int Index;
        public bool EveryFrame;
        public string? OtherVariable;
        public float? OtherConstant;
    }
}
