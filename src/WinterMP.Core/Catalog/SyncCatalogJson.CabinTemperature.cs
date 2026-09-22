using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static CabinTemperatureInputData ParseCabinTemperatureInputs(object? value, VehicleTemperatureSourceData source)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid cabin temperature inputs.");
            var data = new CabinTemperatureInputData {
                CabinPath = TemperaturePath(fields, "cabinPath"), CabinFsm = TemperatureName(fields, "cabinFsm"),
                CabinState = TemperatureName(fields, "cabinState"), HeaterPath = TemperaturePath(fields, "heaterPath"),
                HeaterFsm = TemperatureName(fields, "heaterFsm"), HeaterState = TemperatureName(fields, "heaterState") };
            if (!data.CabinPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                || !data.HeaterPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                || data.CabinPath == data.HeaterPath || data.CabinPath == source.SourcePath || data.HeaterPath == source.SourcePath
                || data.CabinPath == source.GaugePath || data.HeaterPath == source.GaugePath)
                throw new FormatException("Foreign or ambiguous cabin temperature inputs.");
            return data;
        }
    }

    internal sealed class CabinTemperatureInputData
    {
        public string CabinPath = "", CabinFsm = "", CabinState = "", HeaterPath = "", HeaterFsm = "", HeaterState = "";
    }
}
