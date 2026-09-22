using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static GuestEngineInputsData ParseGuestEngineInputs(object? value, ReplacementPartsData? replacements,
            GuestEngineProtectionData? protection)
        {
            if (value is not Dictionary<string, object?> obj || replacements == null || protection == null)
                throw new FormatException("Guest engine inputs require replacement identities and native write protection.");
            var data = new GuestEngineInputsData();
            ParseEngineWires(obj, data);
            data.Battery = ParseEngineBattery(obj, protection);
            data.Block = ParseEngineBlock(obj, protection);
            data.Gearbox = ParseEngineGearbox(obj, protection);
            data.Heater = ParseEngineHeater(obj, protection);
            data.Valves = ParseValveInputs(obj);
            var sources = new Dictionary<string, string[]> {
                { "CoolingAmbient::Cooling", new[] { "TempCar" } },
                { "Grille::Cooling", new[] { "Installed", "CoolingAirRateModifier" } },
                { "GrilleBlockoff::Cooling", new[] { "Installed" } },
                { "Hood::Cooling", new[] { "Installed", "CoolingAirRateModifier" } },
                { "FiberglassHood::Cooling", new[] { "Installed", "CoolingAirRateModifier" } },
                { "CoolantHoseTop::Cooling", new[] { "Tightness" } },
                { "CoolantHoseBottom::Cooling", new[] { "Installed", "Tightness" } },
                { "CoolantHoseInlet::Cooling", new[] { "Tightness" } },
                { "CoolantHoseOutlet::Cooling", new[] { "Tightness" } },
                { "Carburettor::Cooling", new[] { "Tightness" } },
                { "Radiator::Cooling", new[] { "Installed", "Coolant", "PressureCap", "Wear", "FlectEfficiency" } },
                { "RockerCover::Oil", new[] { "Tightness" } },
                { "Oilpan::Oil", new[] { "Wear", "OilViscosity", "Tightness", "Oil" } },
                { "Oilpan::Wearing", new[] { "Oil", "OilContamination" } },
                { "Oilpan::Cylinders", new[] { "OilContamination" } },
                { "ExhaustHeaders::Valves", new[] { "DataPower", "DataTorque", "DataPowerAdd" } },
                { "ExhaustFront::Valves", new[] { "DataPower", "DataTorque", "DataPowerAdd" } },
                { "ExhaustRear::Valves", new[] { "DataPower", "DataTorque", "DataPowerAdd" } },
                { "ExhaustMuffler::Valves", new[] { "DataPower", "DataTorque", "DataPowerAdd" } },
                { "Carburettor::FuelLine", new[] { "Installed", "FuelChamber", "CarbReserve" } },
                { "Carburettor::Mixture", new[] { "SettingMixture" } },
                { "Carburettor::Valves", new[] { "DataPower", "DataTorque", "DataPowerAdd" } },
                { "AirCleaner::FuelLine", new[] { "Installed" } },
                { "AirCleaner::Valves", new[] { "DataPower", "DataTorque", "DataPowerAdd" } },
                { "CylinderHead::Cylinders", new[] { "Installed" } },
                { "Gearbox::Starter", new[] { "Type" } },
                { "EngineBlock::Starter", new[] { "Installed", "Installed" } },
                { "EngineBlock::Oil", new[] { "Wear" } },
                { "EngineBlock::Cooling", new[] { "Damaged" } },
                { "Battery::Electrics", new[] { "Installed", "Charge", "Charge" } },
                { "VIN212::Cylinders", new[] { "Installed" } },
                { "REVLIMITER0::Cylinders", new[] { "Installed", "SettingRPM" } },
                { "VIN120::Cylinders", new[] { "Installed", "InertiaFactor" } },
                { "VIN120::Starter", new[] { "Installed" } },
                { "VIN137::Valves", new[] { "Installed" } },
                { "VIN137::Cooling", new[] { "Installed" } },
                { "VIN134::Cylinders", new[] { "Installed", "Wear" } },
                { "VIN134::Oil", new[] { "Installed" } },
                { "VIN129::Cooling", new[] { "Installed", "Wear" } },
                { "VIN128::Cooling", new[] { "Tightness" } },
                { "OILFILTR0::Oil", new[] { "Tightness", "Dirt" } },
                { "VIN102::Cylinders", new[] { "Installed", "Wear" } },
                { "VIN102::Oil", new[] { "Wear" } },
                { "VIN102::Wearing", new[] { "Wear" } },
                { "VIN105::Cylinders", new[] { "Installed" } },
                { "VIN109::Cylinders", new[] { "Installed" } },
                { "VIN110::Cylinders", new[] { "Installed" } },
                { "VIN110::FuelLine", new[] { "Wear" } },
                { "VIN107::Cylinders", new[] { "Installed", "Wear" } },
                { "VIN131::Cylinders", new[] { "Installed", "Wear", "Tightness", "SparkAngle" } },
                { "VIN130::Starter", new[] { "Installed", "Wear", "Durability" } },
                { "VIN126::Oil", new[] { "Installed", "Wear", "Durability" } },
                { "VIN126::Cooling", new[] { "Installed", "Wear", "Tightness", "Efficiency" } },
                { "VIN125::FuelLine", new[] { "Installed", "Wear", "OutputRate" } },
                { "VIN125::Wearing", new[] { "Durability" } },
                { "VIN132::Oil", new[] { "Installed", "Wear" } },
                { "VIN132::Wearing", new[] { "Durability" } },
                { "FANBELT0::Oil", new[] { "Installed" } },
                { "FANBELT0::Valves", new[] { "Installed" } },
                { "FANBELT0::Cooling", new[] { "Installed", "Installed" } },
                { "FANBELT0::Electrics", new[] { "Installed", "Installed" } },
                { "VIN133::Oil", new[] { "Installed", "Wear", "Friction" } },
                { "VIN133::Electrics", new[] { "Efficiency", "Installed", "Damaged", "Durability", "Wear", "Damaged", "Installed" } },
                { "VIN115::Cylinders", new[] { "Installed", "Wear", "ValveTolerance" } },
                { "VIN115::Wearing", new[] { "Durability" } },
                { "VIN115::Valves", new[] { "ValveTolerance", "CamProfile" } } };
            for (int slot = 1; slot <= 8; slot++) sources.Add("VIN117::Cylinders::" + slot, new[] { "Bolted" });
            for (int slot = 1; slot <= 5; slot++) sources.Add("VIN104::Wearing::" + slot, new[] { "Wear" });
            for (int slot = 1; slot <= 4; slot++)
            {
                sources.Add("VIN103::Cylinders::" + slot, new[] { "Installed", "Wear" });
                sources.Add("VIN103::Mixture::" + slot, new[] { "Wear" });
                sources.Add("SPRKPLUG0::Cylinders::" + slot, new[] { "Installed", "Wear", "Tightness", "Durability" });
            }
            var consumerSources = new HashSet<string>();
            AddEngineWireSources(sources);
            var consumerActions = new HashSet<string>();
            foreach (var fields in EngineInputEntries(obj, "entries", sources.Count))
            {
                string sourceKey = EngineInputSourceKey(fields);
                var entry = new GuestEngineInputData {
                    FamilyPrefix = EngineInputName(fields, sourceKey), MountPath = EngineInputPath(fields, "mountPath"),
                    MountVariable = sourceKey == "familyPrefix" ? EngineInputName(fields, "mountVariable") : "",
                    ReaderPath = EngineInputPath(fields, "readerPath"), Fsm = EngineInputName(fields, "fsm"),
                    InputFsm = EngineInputName(fields, "inputFsm"), DirectTarget = fields.ContainsKey("directTarget"),
                    TargetVariable = fields.ContainsKey("directTarget") ? "" : EngineInputName(fields, "targetVariable") };
                if (fields.ContainsKey("wire")) BindEngineWireSource(fields, entry, data);
                if (fields.ContainsKey("battery")) BindEngineBatterySource(fields, entry, data.Battery);
                if (fields.ContainsKey("block")) BindEngineBlockSource(fields, entry, data.Block);
                if (fields.ContainsKey("gearbox")) BindEngineGearboxSource(fields, entry, data.Gearbox);
                if (fields.ContainsKey("head")) BindCylinderHeadSource(fields, entry, data.Block.Head);
                if (fields.ContainsKey("carburettor")) BindCarburettorSource(fields, entry, data.Block.Carburettor);
                if (fields.ContainsKey("airCleaner")) BindAirCleanerSource(fields, entry, data.Block.AirCleaner);
                if (fields.ContainsKey("coolingAmbient")) BindCoolingAmbientSource(fields, entry, data.Block.CoolingAmbient);
                if (fields.ContainsKey("coolingAirflow")) BindCoolingAirflowSource(fields, entry, data.Block.CoolingAirflow);
                if (fields.ContainsKey("coolantHose")) BindCoolantHoseSource(fields, entry, data.Block.CoolantHoses);
                if (fields.ContainsKey("radiator")) BindRadiatorSource(fields, entry, data.Block.Radiator);
                if (fields.ContainsKey("rockerCover")) BindRockerCoverSource(fields, entry, data.Block.RockerCover);
                if (fields.ContainsKey("oilpan")) BindOilpanSource(fields, entry, data.Block.Oilpan);
                if (fields.ContainsKey("exhaust")) BindExhaustSource(fields, entry, data.Block.Exhaust);
                if (entry.DirectTarget && (entry.BlockSource == null || fields["directTarget"] is not bool direct || !direct))
                    throw new FormatException("Unsupported direct engine input target.");
                string source = entry.FamilyPrefix + "::" + entry.Fsm;
                int slotCount = entry.FamilyPrefix == "VIN117" ? 8 : entry.FamilyPrefix == "VIN104" ? 5
                    : entry.FamilyPrefix == "VIN103" || entry.FamilyPrefix == "SPRKPLUG0" ? 4 : 0;
                string slotReference = entry.FamilyPrefix == "VIN117" ? "Rockers" : entry.FamilyPrefix == "VIN104" ? "MainBearings"
                    : entry.FamilyPrefix == "SPRKPLUG0" ? "Sparkplugs" : "Pistons";
                if (slotCount != 0)
                {
                    entry.SlotIndex = (byte)SlotNumber(fields.TryGetValue("slotIndex", out var slot) ? slot : null, "engine input slot", 1, slotCount);
                    if (entry.MountVariable != replacements["slotDatabaseVariable"])
                        throw new FormatException("Slotted engine inputs require the native assembly database.");
                    source += "::" + entry.SlotIndex;
                }
                else if (fields.ContainsKey("slotIndex")) throw new FormatException("Unsupported slotted engine input family.");
                if (!sources.TryGetValue(source, out var inputs) || entry.InputFsm != (entry.CoolingAmbientSource != null ? entry.CoolingAmbientSource.Fsm : replacements["itemFsm"])
                    || replacements["mountInstalledVariable"] != "Installed")
                    throw new FormatException("Unsupported guest engine input family or Data binding.");
                sources.Remove(source);
                string consumer = entry.ReaderPath + "::" + entry.Fsm;
                if (!consumerSources.Add(consumer + "::" + entry.TargetVariable))
                    throw new FormatException("Guest engine input sources must be unique within a consumer.");
                var prefixes = new List<string>();
                if (entry.WiringSource == null && entry.BatterySource == null && entry.BlockSource == null && entry.GearboxSource == null && entry.HeadSource == null && entry.CarburettorSource == null && entry.AirCleanerSource == null && entry.ExhaustSource == null && entry.OilpanSource == null && entry.RockerCoverSource == null && entry.RadiatorSource == null && entry.CoolantHoseSource == null && entry.CoolingAirflowSource == null && entry.CoolingAmbientSource == null) prefixes.Add(entry.FamilyPrefix);
                if (entry.FamilyPrefix == "VIN125" || entry.FamilyPrefix == "VIN115" || entry.FamilyPrefix == "VIN133" || entry.FamilyPrefix == "VIN120")
                {
                    var expected = entry.FamilyPrefix == "VIN125" ? new[] { "FUELPUMP0" }
                        : entry.FamilyPrefix == "VIN120" ? new[] { "FLYWHEELa0", "FLYWHEELb0", "VIN138" }
                        : entry.FamilyPrefix == "VIN133" ? new[] { "ALTERNATOR0" }
                        : new[] { "CAMTUNEa0", "CAMTUNEb0", "CAMTUNEc0", "CAMTUNEd0" };
                    if (!fields.TryGetValue("alternateFamilies", out var alternatives) || alternatives is not List<object?> names
                        || names.Count != expected.Length)
                        throw new FormatException("Engine inputs require every audited factory variant.");
                    foreach (var name in names)
                    {
                        if (name is not string alternate || Array.IndexOf(expected, alternate) < 0 || prefixes.Contains(alternate))
                            throw new FormatException("Unsupported or duplicate engine input variant.");
                        prefixes.Add(alternate);
                    }
                }
                else if (fields.ContainsKey("alternateFamilies"))
                    throw new FormatException("Unsupported guest engine input variants.");
                foreach (string prefix in prefixes)
                {
                    ReplacementPartFactoryData? family = null;
                    foreach (var candidate in replacements.Factories)
                        if (candidate.Prefix == prefix) family = candidate;
                    if (family == null || (entry.SlotIndex == 0 ? family.SlotCount != 0
                        : family.SlotCount != slotCount || family.SlotReference != slotReference))
                        throw new FormatException("Missing guest engine input family.");
                    bool mountReference = entry.SlotIndex != 0;
                    foreach (var reference in family.References)
                        if (reference.Target == replacements["installPointVariable"] && reference.Source == entry.MountVariable)
                            mountReference = true;
                    if (!mountReference) throw new FormatException("Guest engine input mount must use the replacement factory reference.");
                    foreach (string input in inputs)
                        if (input == "CamProfile" ? family.CamProfileVariable != input
                            : input == "Damaged" ? family.AlternatorDamageVariable != input
                            : input == "Bolted" ? Array.IndexOf(family.Scalars, "Tightness") < 0
                            : input != "Installed" && Array.IndexOf(family.Scalars, input) < 0)
                            throw new FormatException("Guest engine input must be published by every supported variant.");
                    entry.Families.Add(family);
                }
                GuestEngineWriterData? protectedWriter = null;
                foreach (var writer in protection.Writers)
                    if (writer.Path == entry.ReaderPath && writer.Fsm == entry.Fsm) protectedWriter = writer;
                if (protectedWriter == null)
                    throw new FormatException("Guest engine input consumer must have native write protection.");
                var required = new List<string>(inputs);
                var actions = new HashSet<string>();
                var outputs = new Dictionary<string, string>();
                foreach (var fieldsReader in EngineInputEntries(fields, "readers", required.Count))
                {
                    var reader = new GuestEngineInputReaderData {
                        State = EngineInputName(fieldsReader, "state"),
                        ActionIndex = SlotNumber(fieldsReader.TryGetValue("actionIndex", out var index) ? index : null, "engine input action index", 0, 255),
                        ActionType = EngineInputName(fieldsReader, "actionType"), Variable = EngineInputName(fieldsReader, "variable"),
                        Output = EngineInputName(fieldsReader, "output") };
                    if (!fieldsReader.TryGetValue("everyFrame", out var everyFrame) || everyFrame is not bool frame)
                        throw new FormatException("Missing guest engine input timing.");
                    reader.EveryFrame = frame;
                    if (entry.CarburettorSource != null) ValidateCarburettorReader(entry, reader);
                    if (entry.AirCleanerSource != null) ValidateAirCleanerReader(entry, reader);
                    if (entry.CoolingAmbientSource != null) ValidateCoolingAmbientReader(entry, reader);
                    if (entry.CoolingAirflowSource != null) ValidateCoolingAirflowReader(entry, reader);
                    if (entry.CoolantHoseSource != null) ValidateCoolantHoseReader(entry, reader);
                    if (entry.RadiatorSource != null) ValidateRadiatorReader(entry, reader);
                    if (entry.RockerCoverSource != null) ValidateRockerCoverReader(entry, reader);
                    if (entry.OilpanSource != null) ValidateOilpanReader(entry, reader);
                    if (entry.ExhaustSource != null) ValidateIntakePerformanceReader(reader, entry.ExhaustSource.State);
                    if (entry.HeadSource != null && (reader.State != "Powertrain" || reader.ActionIndex != 0
                        || reader.Output != "Installed1" || reader.EveryFrame))
                        throw new FormatException("Unsupported native cylinder-head read.");
                    if (entry.GearboxSource != null && (reader.State != "Check automatic" || reader.ActionIndex != 0
                        || reader.Output != "Automatic" || reader.EveryFrame))
                        throw new FormatException("Unsupported native gearbox starter read.");
                    if (!required.Remove(reader.Variable) || (reader.Variable == "Installed" || reader.Variable == "Bolted" || reader.Variable == "Damaged"
                        ? reader.ActionType != "GetFsmBool"
                        : reader.Variable == "Type" ? reader.ActionType != "GetFsmInt" : reader.Variable == "CamProfile" ? reader.ActionType != "GetFsmString" : reader.ActionType != "GetFsmFloat"))
                        throw new FormatException("Guest engine input must match the family's authoritative fields.");
                    if (!actions.Add(reader.State + "::" + reader.ActionIndex)
                        || (outputs.TryGetValue(reader.Output, out var outputVariable) && outputVariable != reader.Variable)
                        || !consumerActions.Add(consumer + "::" + reader.State + "::" + reader.ActionIndex))
                        throw new FormatException("Duplicate guest engine input action or output.");
                    outputs[reader.Output] = reader.Variable;
                    foreach (var action in protectedWriter.Actions)
                        if (action.State == reader.State && action.Index == reader.ActionIndex)
                            throw new FormatException("Guest engine reader overlaps a protected write action.");
                    foreach (var action in protectedWriter.PoseActions)
                        if (action.State == reader.State && action.Index == reader.ActionIndex)
                            throw new FormatException("Guest engine reader overlaps a protected pose action.");
                    entry.Readers.Add(reader);
                }
                data.Entries.Add(entry);
            }
            return data;
        }

        private static string EngineInputSourceKey(Dictionary<string, object?> fields)
        {
            string? found = null;
            foreach (string key in new[] { "familyPrefix", "wire", "battery", "block", "gearbox", "head", "carburettor", "airCleaner", "exhaust", "oilpan", "rockerCover", "radiator", "coolantHose", "coolingAirflow", "coolingAmbient" })
                if (fields.ContainsKey(key))
                {
                    if (found != null) throw new FormatException("Ambiguous guest engine input source.");
                    found = key;
                }
            return found ?? throw new FormatException("Missing guest engine input source.");
        }

        private static IEnumerable<Dictionary<string, object?>> EngineInputEntries(Dictionary<string, object?> obj, string key, int count)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> entries || entries.Count != count)
                throw new FormatException("Invalid guest engine input " + key + ".");
            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> fields)
                    throw new FormatException("Invalid guest engine input " + key + " entry.");
                yield return fields;
            }
        }

        private static string EngineInputPath(Dictionary<string, object?> obj, string key)
        {
            string path = RequiredString(obj, key);
            if (path.Length > 512 || !ValidScenePath(path) || path.IndexOf(':') >= 0)
                throw new FormatException("Invalid guest engine input " + key + ".");
            foreach (string part in path.Split('/'))
                if (part == "." || part.Trim().Length == 0)
                    throw new FormatException("Invalid guest engine input path segment.");
            foreach (char ch in path)
                if (char.IsControl(ch)) throw new FormatException("Invalid guest engine input path.");
            return path;
        }

        private static string EngineInputName(Dictionary<string, object?> obj, string key)
        {
            string name = RequiredString(obj, key);
            if (name.Trim().Length == 0 || name.Length > 128 || name.IndexOfAny(new[] { ':', '/', '\\' }) >= 0)
                throw new FormatException("Invalid guest engine input " + key + ".");
            foreach (char ch in name)
                if (char.IsControl(ch)) throw new FormatException("Invalid guest engine input " + key + ".");
            return name;
        }
    }

    internal sealed class GuestEngineInputsData
    {
        public readonly List<GuestEngineInputData> Entries = new List<GuestEngineInputData>();
        public readonly List<EngineWireData> Wires = new List<EngineWireData>();
        public EngineBatteryData? Battery;
        public EngineBlockData? Block;
        public EngineGearboxData? Gearbox;
        public EngineHeaterData? Heater;
        public GuestValveInputsData Valves = null!;
    }

    internal sealed class GuestEngineInputData
    {
        public uint FactoryId => Families[0].Identity.FactoryId;
        public readonly List<ReplacementPartFactoryData> Families = new List<ReplacementPartFactoryData>();
        public string FamilyPrefix = "", MountPath = "", MountVariable = "", ReaderPath = "", Fsm = "", InputFsm = "", TargetVariable = "";
        public byte SlotIndex;
        public EngineWireData? WiringSource;
        public EngineBatteryData? BatterySource;
        public EngineBlockData? BlockSource;
        public EngineGearboxData? GearboxSource;
        public EngineExhaustPartData? ExhaustSource;
        public EngineCoolingAmbientData? CoolingAmbientSource;
        public EngineCoolingAirflowData? CoolingAirflowSource;
        public EngineCoolantHoseData? CoolantHoseSource;
        public EngineMountedPartData? RadiatorSource, RockerCoverSource, OilpanSource, HeadSource, CarburettorSource, AirCleanerSource;
        public bool DirectTarget;
        public readonly List<GuestEngineInputReaderData> Readers = new List<GuestEngineInputReaderData>();
    }

    internal sealed class GuestEngineInputReaderData
    {
        public string State = "", ActionType = "", Variable = "", Output = "";
        public int ActionIndex;
        public bool EveryFrame;
    }
}
