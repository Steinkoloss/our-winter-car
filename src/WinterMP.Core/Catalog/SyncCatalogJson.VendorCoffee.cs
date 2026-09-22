using System;
using System.Collections.Generic;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static VendorCoffeeData ParseVendorCoffee(object? value)
        {
            var section = VendorObject(value, "vendorCoffee", "schemaVersion", "machines");
            if (!(section["schemaVersion"] is long version) || version != 1) throw new FormatException("Unsupported vendorCoffee schemaVersion.");
            if (!(section["machines"] is List<object?> machines) || machines.Count != 1) throw new FormatException("vendorCoffee requires exactly one INSPECTION machine.");
            var machine = VendorObject(machines[0], "machine", "root", "roles", "native");
            if (RequiredString(machine, "root") != VendorCoffeePolicy.InspectionRoot) throw new FormatException("vendorCoffee root exceeds audited scope.");
            var data = new VendorCoffeeData(RequiredString(machine, "root"));
            var roles = VendorObject(machine["roles"], "roles", "buy", "acquire", "target", "cup");
            AddVendorRole(data, roles, "buy", "Functions/CoffeeButton", "Buy",
                new[] { "Wait player", "Wait button", "Purchase" }, new[] { "String:Notification", "GameObject:Pan" });
            AddVendorRole(data, roles, "acquire", "Functions/GetACup", "Use",
                new[] { "Wait player", "Wait button", "State 1", "State 2" }, new string[0]);
            AddVendorRole(data, roles, "target", "Functions/PanTarget", "Data",
                new[] { "ON", "OFF" }, new[] { "Bool:Pouring" });
            AddVendorRole(data, roles, "cup", "Functions/CupPivot/coffee cup(itemx)", "Use",
                new[] { "Wait player", "Wait button", "Cup full?", "Pour", "Delay", "Check drink 2", "Play anim", "State 1", "Data" },
                new[] { "Float:Coffee", "Float:Distance", "Float:Pos", "Float:Scale", "Bool:Pouring", "GameObject:HandDrink", "GameObject:Mesh", "GameObject:Pivot", "GameObject:TargetPan" });
            var native = VendorObject(machine["native"], "native", data.MissingNativeFields);
            foreach (string field in data.MissingNativeFields)
                if (native[field] != null) throw new FormatException("vendorCoffee.native." + field + ": schema 1 requires unresolved null; serialized extraction and a typed native adapter are not implemented.");
            return data;
        }
        private static Dictionary<string, object?> VendorObject(object? value, string label, params string[] keys)
        {
            if (!(value is Dictionary<string, object?> obj) || obj.Count != keys.Length) throw new FormatException("Invalid vendorCoffee " + label + " fields.");
            foreach (string key in keys) if (!obj.ContainsKey(key)) throw new FormatException("Missing vendorCoffee " + label + "." + key);
            return obj;
        }
        private static void AddVendorRole(VendorCoffeeData data, Dictionary<string, object?> roles, string role, string suffix, string fsm, string[] states, string[] variables)
        {
            var obj = VendorObject(roles[role], role, "suffix", "fsm", "requiredStates", "requiredVariables");
            if (RequiredString(obj, "suffix") != suffix || RequiredString(obj, "fsm") != fsm) throw new FormatException("vendorCoffee " + role + " binding drift.");
            VendorNames(obj["requiredStates"], states); VendorNames(obj["requiredVariables"], variables);
            data.Roles.Add(role, new VendorCoffeeRole(RequiredString(obj, "suffix"), RequiredString(obj, "fsm"), states, variables));
        }
        private static void VendorNames(object? value, string[] expected)
        {
            if (!(value is List<object?> entries) || entries.Count != expected.Length) throw new FormatException("vendorCoffee required native schema drift.");
            for (int i = 0; i < expected.Length; i++) if (!(entries[i] is string text) || text != expected[i]) throw new FormatException("vendorCoffee required native schema drift.");
        }
    }
    internal sealed class VendorCoffeeRole
    {
        internal readonly string Suffix, Fsm;
        internal readonly string[] RequiredStates, RequiredVariables;
        internal VendorCoffeeRole(string suffix, string fsm, string[] states, string[] variables)
        { Suffix = suffix; Fsm = fsm; RequiredStates = states; RequiredVariables = variables; }
    }
    internal sealed class VendorCoffeeData
    {
        internal readonly string Root;
        internal readonly Dictionary<string, VendorCoffeeRole> Roles = new Dictionary<string, VendorCoffeeRole>(StringComparer.Ordinal);
        internal readonly string[] MissingNativeFields = { "price", "references", "actions", "cupLifecycle", "playerEffect", "persistence", "initialization", "geometry" };
        internal VendorCoffeeData(string root) { Root = root; }
        // No JSON switch, signature string or actionTypes list may enable an
        // unaudited native mutation. Schema 1 is an explicitly incomplete contract.
        internal bool RuntimeEnabled => false;
        internal string DisabledReason => "native adapter unavailable; missing serialized fields: " + string.Join(", ", MissingNativeFields);
    }
}
