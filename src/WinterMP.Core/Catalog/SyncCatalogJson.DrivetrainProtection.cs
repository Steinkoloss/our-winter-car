using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static void ParseDrivetrainProtection(Dictionary<string, object?> entry, GuestEngineWriterData writer, HashSet<string> actions)
        {
            if (entry.ContainsKey("eventActions"))
                foreach (var row in EngineProtectionEntries(entry, "eventActions", 1, 16))
                {
                    var action = new GuestEngineEventActionData { State = EngineProtectionName(row, "state"),
                        Index = SlotNumber(row.TryGetValue("index", out var index) ? index : null, "protected event index", 0, 255),
                        TargetVariable = EngineProtectionName(row, "targetVariable"), TargetFsm = EngineProtectionName(row, "targetFsm"),
                        Event = EngineProtectionName(row, "event") };
                    if (!actions.Add(action.State + "::" + action.Index)) throw new FormatException("Protected event overlaps another action.");
                    writer.EventActions.Add(action);
                }
            if (!entry.ContainsKey("drivetrainWear"))
            {
                if (entry.ContainsKey("gearboxOil")) throw new FormatException("Gearbox oil requires guarded drivetrain wear inputs.");
                return;
            }
            bool transmission = writer.Path == "CORRIS/Simulation/Systems/Drivetrain" && writer.Fsm == "Transmission";
            bool automatic = writer.Path == "CORRIS/Simulation/Systems/Drivetrain/GearboxAutomatic" && writer.Fsm == "3 speed";
            if (!transmission && !automatic) throw new FormatException("Unsupported drivetrain wear consumer.");
            var required = new HashSet<string>(transmission ? new[] { "Driveshaft#2:0:db_Driveshaft:Wear", "Gearbox damage#0:1:db_Gearbox:Wear", "Rear axle#1:2:db_Rearaxle:Wear" }
                : new[] { "Set stall speed#3:1:db_Gearbox:GearboxWear" });
            foreach (var row in EngineProtectionEntries(entry, "drivetrainWear", required.Count, required.Count))
            {
                var read = new GuestDrivetrainReadData { State = EngineProtectionName(row, "state"),
                    Index = SlotNumber(row.TryGetValue("index", out var index) ? index : null, "drivetrain reader index", 0, 255),
                    Part = SlotNumber(row.TryGetValue("part", out var part) ? part : null, "drivetrain part", 0, 2),
                    Variable = EngineProtectionName(row, "variable"), Output = EngineProtectionName(row, "output") };
                if (!required.Remove(read.State + "#" + read.Index + ":" + read.Part + ":" + read.Variable + ":" + read.Output)
                    || !actions.Add(read.State + "::" + read.Index)) throw new FormatException("Changed or overlapping drivetrain wear reader.");
                writer.DrivetrainWearReads.Add(read);
            }
            if (automatic)
            {
                if (!entry.TryGetValue("gearboxOil", out var oil) || oil is not Dictionary<string, object?> row)
                    throw new FormatException("Automatic gearbox requires its oil reader.");
                var read = new GuestDrivetrainReadData { State = EngineProtectionName(row, "state"),
                    Index = SlotNumber(row.TryGetValue("index", out var index) ? index : null, "gearbox oil index", 0, 255),
                    Variable = EngineProtectionName(row, "variable"), Output = EngineProtectionName(row, "output"), Part = 1, Scalar = "OilLevel" };
                if (read.State != "Set stall speed" || read.Index != 0 || read.Variable != "db_Gearbox" || read.Output != "Oil"
                    || !actions.Add(read.State + "::" + read.Index)) throw new FormatException("Changed or overlapping gearbox oil reader.");
                writer.GearboxOilRead = read;
            }
            else if (entry.ContainsKey("gearboxOil")) throw new FormatException("Unsupported gearbox oil consumer.");
            bool damage = false, firstOil = false, secondOil = false, detach = false;
            foreach (var action in writer.Actions)
            {
                damage |= action.State == "Gearbox damage" && action.Index == 5 && action.ActionType == "SetFsmInt"
                    && action.TargetVariable == "db_Gearbox" && action.TargetFsm == "Data" && action.TargetScalar == "DamageType";
                if (action.ActionType == "SubtractFsmFloat" && action.Index == 3 && action.TargetVariable == "db_Gearbox"
                    && action.TargetFsm == "Data" && action.TargetScalar == "OilLevel" && action.GearboxOilUse)
                { firstOil |= action.State == "State 1"; secondOil |= action.State == "State 3"; }
            }
            foreach (var action in writer.EventActions)
                detach |= action.State == "Shaft break" && action.Index == 0 && action.TargetVariable == "db_Driveshaft" && action.TargetFsm == "Data" && action.Event == "BREAKOFF";
            if (transmission ? !damage || !detach : !firstOil || !secondOil)
                throw new FormatException("Drivetrain readers require their saved-write and failure-event guards.");
        }

        private static void ValidateDrivetrainDestinations(GuestEngineProtectionData data)
        {
            bool consumer = false, destination = false;
            foreach (var writer in data.Writers) consumer |= writer.DrivetrainWearReads.Count != 0;
            foreach (var paused in data.PausedFsms)
                destination |= paused.Path == "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox" && paused.Fsm == "Data"
                    && paused.BlockExternalIntWrites && paused.BlockExternalFloatWrites;
            if (consumer && !destination) throw new FormatException("Drivetrain consumers require saved gearbox destination protection.");
        }
    }
    internal sealed class GuestEngineEventActionData
    {
        public string State = "", TargetVariable = "", TargetFsm = "", Event = "";
        public int Index;
    }
    internal sealed class GuestDrivetrainReadData
    {
        public string State = "", Variable = "", Output = "";
        public string Scalar = "Wear";
        public int Index, Part;
    }
}
