using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static ElectricalTemperatureInputData ParseElectricalTemperatureInputs(object? value, VehicleTemperatureSourceData source)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid electrical temperature inputs.");
            var data = new ElectricalTemperatureInputData {
                ElectricsPath = TemperaturePath(fields, "electricsPath"), ElectricsFsm = TemperatureName(fields, "electricsFsm"),
                BatteryState = TemperatureName(fields, "batteryState"), ChargingState = TemperatureName(fields, "chargingState"),
                InteriorPath = TemperaturePath(fields, "interiorPath"), InteriorFsm = TemperatureName(fields, "interiorFsm"),
                InteriorBatteryState = TemperatureName(fields, "interiorBatteryState") };
            if (!data.ElectricsPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                || !data.InteriorPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                || data.ElectricsPath == data.InteriorPath || data.ElectricsPath == source.SourcePath || data.InteriorPath == source.SourcePath
                || data.ElectricsPath == source.GaugePath || data.InteriorPath == source.GaugePath || data.BatteryState == data.ChargingState)
                throw new FormatException("Foreign or ambiguous electrical temperature inputs.");
            return data;
        }
    }

    internal sealed class ElectricalTemperatureInputData
    {
        public string ElectricsPath = "", ElectricsFsm = "", BatteryState = "", ChargingState = "";
        public string InteriorPath = "", InteriorFsm = "", InteriorBatteryState = "";
    }
}
