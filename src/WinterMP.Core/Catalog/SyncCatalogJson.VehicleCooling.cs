using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VehicleCoolingData ParseVehicleCooling(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid vehicle speed profile.");
            var data = new VehicleCoolingData {
                RootPath = TemperaturePath(obj, "rootPath"), ProducerPath = TemperaturePath(obj, "producerPath"),
                ProducerFsm = TemperatureName(obj, "producerFsm"), ProducerState = TemperatureName(obj, "producerState"),
                SpeedGlobal = TemperatureName(obj, "speedGlobal"), CoolingPath = TemperaturePath(obj, "coolingPath"),
                CoolingFsm = TemperatureName(obj, "coolingFsm"), AirState = TemperatureName(obj, "airState"),
                CheckState = TemperatureName(obj, "checkState"), RpmGlobal = TemperatureName(obj, "rpmGlobal"),
                PumpState = TemperatureName(obj, "pumpState"), FanState = TemperatureName(obj, "fanState"),
                LeakState = TemperatureName(obj, "leakState") };
            if (!data.ProducerPath.StartsWith(data.RootPath + "/", StringComparison.Ordinal)
                || !data.CoolingPath.StartsWith(data.RootPath + "/", StringComparison.Ordinal)
                || data.ProducerPath == data.CoolingPath || data.SpeedGlobal == data.RpmGlobal
                || new HashSet<string> { data.AirState, data.CheckState, data.PumpState, data.FanState, data.LeakState }.Count != 5)
                throw new FormatException("Aliased or foreign vehicle cooling references.");
            return data;
        }
    }

    internal sealed class VehicleCoolingData
    {
        public string RootPath = "", ProducerPath = "", ProducerFsm = "", ProducerState = "", SpeedGlobal = "";
        public string CoolingPath = "", CoolingFsm = "", AirState = "", CheckState = "";
        public string RpmGlobal = "", PumpState = "", FanState = "", LeakState = "";
    }
}
