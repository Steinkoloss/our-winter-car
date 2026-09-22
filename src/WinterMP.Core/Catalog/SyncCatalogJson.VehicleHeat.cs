using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleHeatData ParseVehicleHeat(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid vehicle heat profile.");
            var data = new VehicleHeatData {
                RootPath = TemperaturePath(obj, "rootPath"), Path = TemperaturePath(obj, "path"),
                Fsm = TemperatureName(obj, "fsm"), RunningState = TemperatureName(obj, "runningState"),
                StoppedState = TemperatureName(obj, "stoppedState"), ObjectVariable = TemperatureName(obj, "objectVariable"),
                ComponentType = TemperatureName(obj, "componentType"), TorqueMember = TemperatureName(obj, "torqueMember"),
                TorqueVariable = TemperatureName(obj, "torqueVariable"), RpmGlobal = TemperatureName(obj, "rpmGlobal"),
                TemperatureGlobal = TemperatureName(obj, "temperatureGlobal") };
            if (!data.Path.StartsWith(data.RootPath + "/", StringComparison.Ordinal) || data.RunningState == data.StoppedState
                || data.RpmGlobal == data.TemperatureGlobal || data.TorqueVariable == data.RpmGlobal || data.TorqueVariable == data.TemperatureGlobal)
                throw new FormatException("Aliased or foreign heat inputs.");
            return data;
        }
    }
    internal sealed class VehicleHeatData
    {
        public string RootPath = "", Path = "", Fsm = "", RunningState = "", StoppedState = "";
        public string ObjectVariable = "", ComponentType = "", TorqueMember = "", TorqueVariable = "", RpmGlobal = "", TemperatureGlobal = "";
    }
}
