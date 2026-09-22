using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleElectricalData ParseVehicleElectrical(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid vehicle electrical RPM profile.");
            var data = new VehicleElectricalData {
                RootPath = TemperaturePath(fields, "rootPath"), Path = TemperaturePath(fields, "path"),
                Fsm = TemperatureName(fields, "fsm"), RpmGlobal = TemperatureName(fields, "rpmGlobal"),
                RunningState = TemperatureName(fields, "runningState"), ChargingState = TemperatureName(fields, "chargingState"),
                BatteryState = TemperatureName(fields, "batteryState") };
            if (!data.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal)
                || new HashSet<string> { data.RunningState, data.ChargingState, data.BatteryState }.Count != 3)
                throw new FormatException("Foreign or ambiguous electrical RPM references.");
            return data;
        }
    }

    internal sealed class VehicleElectricalData
    {
        public string RootPath = "", Path = "", Fsm = "", RpmGlobal = "";
        public string RunningState = "", ChargingState = "", BatteryState = "";
    }
}
