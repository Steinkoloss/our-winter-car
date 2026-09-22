using System;
using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static void ParseEngineWires(Dictionary<string, object?> obj, GuestEngineInputsData data)
        {
            var ids = new HashSet<uint>();
            foreach (var fields in EngineInputEntries(obj, "wires", (int)WiringPolicy.SourceCount))
            {
                uint id = (uint)SlotNumber(fields.TryGetValue("id", out var number) ? number : null, "engine wire id", 1, (int)WiringPolicy.SourceCount);
                var wire = new EngineWireData { Id = id, Name = EngineInputName(fields, "name"),
                    Path = EngineInputPath(fields, "path"), Fsm = EngineInputName(fields, "fsm"),
                    SettledState = EngineInputName(fields, "settledState") };
                if (!fields.TryGetValue("supportsBolted", out var bolted) || bolted is not bool supportsBolted
                    || !ids.Add(id) || wire.Name != WiringPolicy.Name(id) || wire.Path != "CORRIS/Wiring/DatabaseWiring/" + wire.Name
                    || wire.Fsm != "Data" || supportsBolted != WiringPolicy.SupportsBolted(id)
                    || wire.SettledState != (supportsBolted ? "Set bolt" : "Basic state"))
                    throw new FormatException("Unsupported engine wire identity or native load boundary.");
                if (fields.TryGetValue("projectNativeReads", out var project))
                {
                    if (project is not bool enabled) throw new FormatException("Invalid native wire input projection.");
                    wire.ProjectNativeReads = enabled;
                }
                if (wire.ProjectNativeReads != WiringPolicy.ProjectsNativeReads(id))
                    throw new FormatException("Missing or unsupported native wire read projection.");
                wire.SupportsBolted = supportsBolted;
                if (fields.TryGetValue("connection", out var connection))
                {
                    if (id != 5 || connection is not Dictionary<string, object?> names)
                        throw new FormatException("Unsupported physical wiring connection.");
                    var c = new WireConnectionData();
                    foreach (string key in new[] { "meshPath", "triggersPath", "prerequisitePath", "statusPath", "toolPath" })
                        c.Names.Add(key, EngineInputPath(names, key));
                    foreach (string key in new[] { "firstEndpoint", "secondEndpoint", "endpointFsm", "finishState", "resetState", "resetEvent",
                        "prerequisiteFsm", "prerequisiteVariable", "statusFsm", "statusState", "statusTarget" })
                        c.Names.Add(key, EngineInputName(names, key));
                    if (c["meshPath"] != "CORRIS/Wiring/Parts/ignition-fusebox"
                        || c["triggersPath"] != "CORRIS/Wiring/Triggers/IgnitionFusebox"
                        || c["firstEndpoint"] != "Fusebox" || c["secondEndpoint"] != "Ignition"
                        || c["prerequisitePath"] != "CORRIS/Assemblies/VINP_SteeringColumn"
                        || c["toolPath"] != "EQUIPMENTS/wiring mess(itemx)")
                        throw new FormatException("Ignition wiring native identity changed.");
                    wire.Connection = c;
                }
                if (id == 5 && wire.Connection == null) throw new FormatException("Missing ignition connection profile.");
                data.Wires.Add(wire);
            }
        }

        private static void AddEngineWireSources(Dictionary<string, string[]> sources)
        {
            sources.Add("WiringCoilHarness::Cylinders", new[] { "Installed" });
            sources.Add("WiringBatteryStarter::Starter", new[] { "Bolted" });
            sources.Add("WiringBatteryHarness::Starter", new[] { "Bolted" });
            sources.Add("WiringBatteryGround::Starter", new[] { "Installed" });
            sources.Add("WiringBatteryHarness::Electrics", new[] { "Bolted" });
            sources.Add("WiringBatteryGround::Electrics", new[] { "Bolted" });
            sources.Add("WiringIgnitionFusebox::Electrics", new[] { "Installed" });
            sources.Add("WiringAlternatorRegulator::Electrics", new[] { "Installed", "Installed" });
            sources.Add("WiringRegulatorHarness::Electrics", new[] { "Installed", "Installed" });
            sources.Add("WiringCoilHarness::Electrics", new[] { "Installed" });
            sources.Add("WiringFueltank::Electrics", new[] { "Installed" });
        }

        private static void BindEngineWireSource(Dictionary<string, object?> fields, GuestEngineInputData entry, GuestEngineInputsData data)
        {
            foreach (var wire in data.Wires) if (wire.Name == entry.FamilyPrefix) entry.WiringSource = wire;
            if (entry.WiringSource == null || entry.MountPath != entry.WiringSource.Path || entry.InputFsm != entry.WiringSource.Fsm
                || fields.ContainsKey("familyPrefix") || fields.ContainsKey("alternateFamilies") || fields.ContainsKey("slotIndex")
                || fields.ContainsKey("mountVariable"))
                throw new FormatException("Engine wiring inputs must use their exact native database source.");
        }
    }

    internal sealed class EngineWireData
    {
        public uint Id;
        public string Name = "", Path = "", Fsm = "", SettledState = "";
        public bool SupportsBolted, ProjectNativeReads;
        public WireConnectionData? Connection;
    }

    internal sealed class WireConnectionData
    {
        internal readonly Dictionary<string, string> Names = new Dictionary<string, string>();
        internal string this[string key] => Names[key];
    }
}
