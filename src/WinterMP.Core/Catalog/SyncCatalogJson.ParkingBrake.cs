using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static ParkingBrakeData ParseParkingBrake(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid parking brake profile.");
            var data = new ParkingBrakeData(); var roots = new HashSet<string>();
            foreach (var entry in RpmEntries(obj, "sources", 16))
            {
                var source = new ParkingBrakeSource {
                    RootPath = RpmPath(entry, "rootPath"), ControlPath = RpmPath(entry, "controlPath"),
                    Fsm = RpmName(entry, "fsm"), Variable = RpmName(entry, "variable"),
                    IdleState = RpmName(entry, "idleState"), IncreaseState = RpmName(entry, "increaseState"),
                    DecreaseState = RpmName(entry, "decreaseState") };
                if (!roots.Add(source.RootPath) || !source.ControlPath.StartsWith(source.RootPath + "/", StringComparison.Ordinal)
                    || source.IncreaseState == source.DecreaseState || source.IdleState == source.IncreaseState || source.IdleState == source.DecreaseState)
                    throw new FormatException("Parking brake must identify one vehicle and distinct native states.");
                data.Sources.Add(source);
            }
            return data;
        }
    }
    internal sealed class ParkingBrakeData
    {
        public readonly List<ParkingBrakeSource> Sources = new List<ParkingBrakeSource>();
    }
    internal sealed class ParkingBrakeSource
    {
        public string RootPath = "", ControlPath = "", Fsm = "", Variable = "", IdleState = "", IncreaseState = "", DecreaseState = "";
    }
}
