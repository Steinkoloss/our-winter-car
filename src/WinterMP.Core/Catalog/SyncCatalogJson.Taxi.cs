using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static TaxiFareData ParseTaxiFare(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid taxi fare catalog.");
            var data = new TaxiFareData();
            foreach (string key in TaxiFareData.Fields) data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }
        private static TaxiMeterData ParseTaxiMeter(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid taxi meter catalog.");
            var data = new TaxiMeterData();
            foreach (string key in TaxiMeterData.Fields) data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }

        private static TaxiServiceData ParseTaxiService(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid taxi service catalog.");
            var data = new TaxiServiceData();
            foreach (string key in TaxiServiceData.Fields) data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }

        private static TaxiPickupData ParseTaxiPickup(object? value)
        {
            if (value is not Dictionary<string, object?> fields) throw new FormatException("Invalid taxi pickup catalog.");
            var data = new TaxiPickupData();
            foreach (string key in TaxiPickupData.Fields) data.Names.Add(key, EngineInputPath(fields, key));
            return data;
        }
    }

    internal sealed class TaxiPickupData
    {
        internal static readonly string[] Fields = { "jobPath", "jobFsm", "customer", "car", "walker", "walkerFsm",
            "farState", "nearState", "carState", "cameraGlobal", "playerGlobal", "vehicleGlobal", "vehicleName" };
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }
    internal sealed class TaxiServiceData
    {
        internal static readonly string[] Fields = { "jobPath", "jobFsm", "meterPath", "meterFsm", "customerVariable",
            "walkerFsm", "ringFsm", "phonePath", "phoneFsm", "callState", "answerState", "closeState", "waitState", "luggageFsm", "luggageResetState", "luggageReleaseState", "luggage0", "luggage1", "luggage2", "luggage3", "luggage4", "luggagePool", "paymentsFsm", "rundownVariable", "rundownLetter", "rundownList", "envelopeFsm", "envelopeOpenState", "sheetVariable", "sheetFsm", "sheetCloseState", "settledState" };
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }

    internal sealed class TaxiFareData
    {
        internal static readonly string[] Fields = { "jobPath", "jobFsm", "meterPath", "meterFsm", "customerVariable", "walkerFsm",
            "terminalVariable", "terminalFsm", "cashVariable", "cashFsm", "boardState", "arrivedState", "chargeState", "collectState", "printState", "takeState", "giveState", "receiptVariable", "ticketVariable", "departureState" };
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }

    internal sealed class TaxiMeterData
    {
        internal static readonly string[] Fields = { "jobPath", "jobFsm", "meterPath", "meterFsm", "knob", "knobFsm",
            "button", "buttonFsm", "display", "displayFsm", "increase", "decrease", "select", "knobIdle",
            "toggle", "reset", "buttonIdle" };
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        internal string this[string key] => Names[key];
    }

}
