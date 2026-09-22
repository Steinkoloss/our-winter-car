using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static ParkingJointData ParseParkingJoint(object? value)
        {
            if (value is not Dictionary<string, object?> obj) throw new FormatException("Invalid parking joint profile.");
            return new ParkingJointData { RootPath = RpmPath(obj, "rootPath"), Fsm = RpmName(obj, "fsm"),
                ReleaseState = RpmName(obj, "releaseState"),
                ReleaseIndex = SlotNumber(obj.TryGetValue("releaseIndex", out var index) ? index : null, "Parking joint release index", 0, 255) };
        }
    }
    internal sealed class ParkingJointData
    {
        public string RootPath = "", Fsm = "", ReleaseState = "";
        public int ReleaseIndex;
    }
}
