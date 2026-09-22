using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static EngineTemperatureInputData ParseEngineTemperatureInputs(object? value, string root)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid engine temperature inputs.");
            var data = new EngineTemperatureInputData {
                FuelPath = TemperaturePath(fields, "fuelPath"), OilPath = TemperaturePath(fields, "oilPath"),
                FuelFsm = TemperatureName(fields, "fuelFsm"), MixtureFsm = TemperatureName(fields, "mixtureFsm"),
                OilFsm = TemperatureName(fields, "oilFsm"), PressureFsm = TemperatureName(fields, "pressureFsm"),
                CarburettorState = TemperatureName(fields, "carburettorState"), PrimingState = TemperatureName(fields, "primingState"),
                DensityState = TemperatureName(fields, "densityState"), ViscosityState = TemperatureName(fields, "viscosityState"),
                PressureState = TemperatureName(fields, "pressureState") };
            if (!data.FuelPath.StartsWith(root + "/", StringComparison.Ordinal) || !data.OilPath.StartsWith(root + "/", StringComparison.Ordinal)
                || data.FuelPath == data.OilPath || data.FuelFsm == data.MixtureFsm || data.OilFsm == data.PressureFsm
                || data.CarburettorState == data.PrimingState)
                throw new FormatException("Foreign or ambiguous engine temperature inputs.");
            return data;
        }
    }

    internal sealed class EngineTemperatureInputData
    {
        public string FuelPath = "", OilPath = "", FuelFsm = "", MixtureFsm = "", OilFsm = "", PressureFsm = "";
        public string CarburettorState = "", PrimingState = "", DensityState = "", ViscosityState = "", PressureState = "";
    }
}
