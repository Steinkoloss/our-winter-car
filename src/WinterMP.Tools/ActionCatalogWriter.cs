using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WinterMP.Tools
{
    /// <summary>
    /// Schema 4 operand inventory. Never evaluates actions, arbitrary properties,
    /// shared scalar values or arbitrary ToString implementations. Unknown is data.
    /// </summary>
    internal sealed class ActionCatalogWriter
    {
        private const string PlayMaker = "HutongGames.PlayMaker.";
        private const int MaxItems = 256;
        private const int MaxDepth = 8;
        private readonly Dictionary<string, int> _pathCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private bool _inventoryReadable = true;

        public ActionCatalogWriter()
        {
            try
            {
                foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                {
                    try
                    {
                        var go = obj as GameObject;
                        if (go == null) continue;
                        string path = ScenePath(go.transform);
                        int count;
                        _pathCounts.TryGetValue(path, out count);
                        _pathCounts[path] = count + 1;
                    }
                    catch { _inventoryReadable = false; }
                }
            }
            catch { _inventoryReadable = false; }
        }

        public void WriteState(JsonWriter json, object? state)
        {
            object? raw;
            var status = Read(state, "Actions", out raw);
            var actions = raw as Array;
            if (status == null && (actions == null || actions.Rank != 1 || actions.GetLowerBound(0) != 0))
                status = Marker("unknown", "actions-not-zero-based-array");
            var records = new List<object?>();
            var types = new List<object?>();
            if (status == null && actions != null)
            {
                for (int i = 0; i < actions.Length; i++)
                {
                    object? action = actions.GetValue(i);
                    string? type = action == null ? null : TypeName(action.GetType());
                    types.Add(type);
                    Dictionary<string, object?> record;
                    try { record = CaptureAction(action); }
                    catch (Exception e) { record = Failure(e); }
                    record["index"] = (long)i;
                    record["type"] = type;
                    if (!record.ContainsKey("fields")) record["fields"] = new List<object?>();
                    records.Add(record);
                }
            }
            json.Key("actionTypes"); Write(json, types);
            json.Key("actionRecordsStatus"); Write(json, status ?? Marker("ok", "observed-action-array"));
            json.Key("actionRecords"); Write(json, records);
        }

        private Dictionary<string, object?> CaptureAction(object? action)
        {
            if (action == null) return Marker("unknown", "null-action-slot");
            Type type = action.GetType();
            var record = Marker("ok", "reflected-action-fields-not-asset-defaults");
            record["enabled"] = Member(action, "Enabled", 0, true);
            var fields = new List<FieldInfo>();
            for (Type? current = type; current != null && current.FullName != PlayMaker + "FsmStateAction" && current != typeof(object); current = current.BaseType)
                fields.AddRange(current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            fields.Sort(delegate(FieldInfo a, FieldInfo b)
            {
                int order = string.CompareOrdinal(TypeName(a.DeclaringType!), TypeName(b.DeclaringType!));
                return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
            });
            var values = new List<object?>();
            record["fields"] = values;
            record["fieldCount"] = (long)fields.Count;
            for (int i = 0; i < fields.Count && i < MaxItems; i++)
            {
                FieldInfo field = fields[i];
                var entry = new Dictionary<string, object?>();
                entry["name"] = field.Name;
                entry["declaringType"] = TypeName(field.DeclaringType!);
                entry["type"] = TypeName(field.FieldType);
                try
                {
                    bool serialized = !field.IsDefined(typeof(NonSerializedAttribute), false)
                        && (field.IsPublic || field.IsDefined(typeof(SerializeField), false));
                    entry["data"] = serialized
                        ? Capture(field.GetValue(action), 0, SafeLiteral(type.FullName, field.Name))
                        : Marker("withheld", "nonserialized-or-private-runtime-field");
                }
                catch (Exception e) { entry["data"] = Failure(e); }
                values.Add(entry);
            }
            if (fields.Count > MaxItems) { record["status"] = "unknown"; record["reason"] = "field-limit"; }
            return record;
        }

        private Dictionary<string, object?> Capture(object? value, int depth, bool literal)
        {
            try
            {
                if (value == null) return Marker("null", "null-reference");
                if (depth > MaxDepth) return Marker("unknown", "depth-limit");
                Type type = value.GetType();
                string name = TypeName(type);
                var node = Marker("ok", "observed-operand-not-asset-default");
                node["type"] = name;
                if (value is UnityEngine.Object) return Reference((UnityEngine.Object)value);
                if (type.IsEnum)
                {
                    node["kind"] = "enum";
                    node["value"] = Enum.Format(type, value, "D");
                    node["symbol"] = Enum.GetName(type, value);
                    return node;
                }
                if (value is Type)
                {
                    node["kind"] = "type"; node["value"] = TypeName((Type)value); return node;
                }
                if (value is string || type.IsPrimitive || value is decimal)
                {
                    if (!literal) return Marker("withheld", "literal-field-not-allowlisted");
                    node["kind"] = "literal";
                    if (value is string)
                    {
                        string text = (string)value;
                        if (text.Length > 512) return Marker("unknown", "string-limit");
                        node["value"] = text;
                    }
                    else if (value is bool) node["value"] = value;
                    else if (value is char) node["value"] = new string((char)value, 1);
                    else
                    {
                        double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                        if (double.IsNaN(number) || double.IsInfinity(number)) return Marker("unknown", "nonfinite-literal");
                        // Decimal text avoids rounding large integral operands through double.
                        node["value"] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    }
                    return node;
                }
                var array = value as Array;
                if (array != null)
                {
                    if (array.Rank != 1 || array.GetLowerBound(0) != 0) return Marker("unknown", "array-shape");
                    node["kind"] = "array";
                    node["length"] = (long)array.Length;
                    var items = new List<object?>();
                    node["items"] = items;
                    for (int i = 0; i < array.Length && i < MaxItems; i++)
                    {
                        try { items.Add(Capture(array.GetValue(i), depth + 1, literal)); }
                        catch (Exception e) { items.Add(Failure(e)); }
                    }
                    if (array.Length > MaxItems) { node["status"] = "unknown"; node["reason"] = "array-limit"; }
                    return node;
                }
                // Inherited wrapper types are supported, but only these known metadata
                // properties are invoked. A failed getter never falls back to private data.
                string? wrapper = WrapperType(type);
                if (wrapper != null)
                {
                    node["kind"] = "fsm-variable";
                    var metadata = new Dictionary<string, object?>();
                    node["metadata"] = metadata;
                    object? variableName, useVariable, isNone;
                    var nameFailure = Read(value, "Name", out variableName);
                    var useFailure = Read(value, "UseVariable", out useVariable);
                    var noneFailure = Read(value, "IsNone", out isNone);
                    metadata["Name"] = nameFailure ?? Capture(variableName, depth + 1, true);
                    metadata["UseVariable"] = useFailure ?? Capture(useVariable, depth + 1, true);
                    metadata["IsNone"] = noneFailure ?? Capture(isNone, depth + 1, true);
                    bool known = nameFailure == null && useFailure == null && noneFailure == null
                        && variableName is string && useVariable is bool && isNone is bool;
                    bool reference = wrapper == "FsmGameObject" || wrapper == "FsmObject";
                    bool shared = !known || (bool)useVariable! || ((string)variableName!).Length != 0 || (bool)isNone!;
                    // Object links are useful even through named variables; they are
                    // explicitly observations, never initial/default/persistent identity.
                    node["value"] = reference || (!shared && literal)
                        ? Member(value, "Value", depth + 1, literal)
                        : Marker("withheld", known ? "named-variable-none-or-disallowed-literal" : "unreadable-variable-binding");
                    return node;
                }
                string[]? members = null;
                if (name == PlayMaker + "FsmOwnerDefault") members = new[] { "OwnerOption", "GameObject" };
                if (name == PlayMaker + "FsmEvent") members = new[] { "Name" };
                if (name == PlayMaker + "FsmEventTarget") members = new[] { "target", "gameObject", "fsmComponent", "fsmName", "excludeSelf", "sendToChildren" };
                if (name == PlayMaker + "FsmProperty") members = new[] { "TargetObject", "TargetTypeName", "PropertyName", "setProperty", "BoolParameter" };
                if (members != null)
                {
                    node["kind"] = "playmaker-metadata";
                    var data = new Dictionary<string, object?>();
                    node["members"] = data;
                    foreach (string member in members) data[member] = Member(value, member, depth + 1, true);
                    if (name == PlayMaker + "FsmProperty")
                        node["omittedParameters"] = Marker("withheld", "only-bool-property-parameter-allowlisted");
                    return node;
                }
                node["kind"] = "unsupported"; node["status"] = "unknown"; node["reason"] = "unsupported-type-no-recursion";
                return node;
            }
            catch (Exception e) { return Failure(e); }
        }

        private static string? WrapperType(Type type)
        {
            for (Type? t = type; t != null; t = t.BaseType)
            {
                string name = TypeName(t);
                foreach (string suffix in new[] { "FsmBool", "FsmInt", "FsmFloat", "FsmString", "FsmGameObject", "FsmObject", "FsmEnum", "FsmVector2", "FsmVector3", "FsmColor", "FsmRect", "FsmQuaternion", "FsmMaterial", "FsmTexture", "FsmArray" })
                    if (name == PlayMaker + suffix) return suffix;
            }
            return null;
        }

        private static bool SafeLiteral(string? action, string field)
        {
            // Raw fields in arbitrary/custom actions can be caches or save payloads.
            // Only allowlisted routing/configuration shapes are read as scalar literals.
            if (action == PlayMaker + "Actions.StringCompare") return field == "string1" || field == "string2" || field == "everyFrame" || field == "ignoreCase";
            if (action == PlayMaker + "Actions.StringSwitch") return field == "stringVariable" || field == "compareTo" || field == "everyFrame";
            if (action == PlayMaker + "Actions.ActivateGameObject") return field == "activate" || field == "recursive" || field == "resetOnExit";
            if (action == PlayMaker + "Actions.GetChild") return field == "childName" || field == "withTag" || field == "everyFrame";
            if (action == PlayMaker + "Actions.FindGameObject") return field == "objectName" || field == "withTag" || field == "everyFrame";
            if (action == PlayMaker + "Actions.SendEvent" || action == PlayMaker + "Actions.SendEventByName") return field == "sendEvent" || field == "everyFrame" || field == "delay";
            if (action != null && action.StartsWith(PlayMaker + "Actions.", StringComparison.Ordinal))
                return field == "fsmName" || field == "variableName" || field == "everyFrame";
            return false;
        }

        private Dictionary<string, object?> Member(object value, string name, int depth, bool literal)
        {
            object? raw;
            return Read(value, name, out raw) ?? Capture(raw, depth, literal);
        }

        private static Dictionary<string, object?>? Read(object? target, string name, out object? value)
        {
            value = null;
            if (target == null) return Marker("unknown", "null-member-owner");
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
                var type = target.GetType();
                var field = type.GetField(name, flags);
                if (field != null) { value = field.GetValue(target); return null; }
                var property = type.GetProperty(name, flags);
                if (property == null) return Marker("unknown", "missing-member");
                if (!property.CanRead || property.GetIndexParameters().Length != 0) return Marker("unknown", "nonreadable-property-shape");
                value = property.GetValue(target, null);
                return null;
            }
            catch (Exception e) { return Failure(e); }
        }

        public Dictionary<string, object?> Reference(UnityEngine.Object obj)
        {
            try
            {
                if (obj == null) return Marker("unknown", "destroyed-unity-object");
                var component = obj as Component;
                var go = obj as GameObject;
                if (component != null) go = component.gameObject;
                if (go == null) return Marker("unknown", "non-scene-unity-object-no-asset-id");
                var node = Marker("ok", "structural-reference-not-persistent-id");
                node["kind"] = "scene-reference";
                node["provenance"] = "runtime-reference-not-default";
                string path = ScenePath(go.transform);
                node["path"] = path;
                int count;
                _pathCounts.TryGetValue(path, out count);
                node["pathCandidates"] = (long)count;
                node["resolution"] = !_inventoryReadable ? "unknown-inventory" : count == 1 ? "unique-observed-path" : count == 0 ? "not-in-inventory" : "ambiguous-path";
                if (!_inventoryReadable || count != 1) node["status"] = "unknown";
                var parts = new List<object?>();
                node["components"] = parts;
                var indices = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var part in go.GetComponents(typeof(Component)))
                {
                    if (part == null) { parts.Add(Marker("unknown", "missing-component-slot")); continue; }
                    string type = TypeName(part.GetType());
                    int index;
                    indices.TryGetValue(type, out index);
                    indices[type] = index + 1;
                    var entry = new Dictionary<string, object?>();
                    entry["type"] = type; entry["index"] = (long)index;
                    if (type == "PlayMakerFSM") entry["fsmName"] = Member(part, "FsmName", 0, true);
                    parts.Add(entry);
                    if (object.ReferenceEquals(part, component))
                    {
                        node["componentType"] = type;
                        node["componentIndex"] = (long)index;
                        if (type == "PlayMakerFSM")
                        {
                            object? fsmName;
                            var failure = Read(part, "FsmName", out fsmName);
                            node["fsmName"] = fsmName as string;
                            if (failure != null) node["fsmNameStatus"] = failure;
                        }
                    }
                }
                if (component != null && !node.ContainsKey("componentIndex"))
                {
                    node["status"] = "unknown"; node["reason"] = "component-not-in-owner-list";
                }
                return node;
            }
            catch (Exception e) { return Failure(e); }
        }

        private static string ScenePath(Transform transform)
        {
            string path = transform.name;
            int depth = 0;
            while (transform.parent != null)
            {
                if (++depth > 256) throw new InvalidOperationException();
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string TypeName(Type type) { return type.FullName ?? type.Name; }

        internal static Dictionary<string, object?> Marker(string status, string reason)
        {
            return new Dictionary<string, object?> { { "status", status }, { "reason", reason } };
        }

        private static Dictionary<string, object?> Failure(Exception e)
        {
            var marker = Marker("unreadable", "reflection-or-reference-read-failed");
            marker["errorType"] = TypeName((e is TargetInvocationException && e.InnerException != null ? e.InnerException : e).GetType());
            return marker;
        }

        internal static void Write(JsonWriter json, object? value)
        {
            var map = value as Dictionary<string, object?>;
            if (map != null)
            {
                json.BeginObject();
                var keys = new List<string>(map.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys) { json.Key(key); Write(json, map[key]); }
                json.EndObject();
            }
            else if (value is List<object?>)
            {
                json.BeginArray();
                foreach (object? item in (List<object?>)value) Write(json, item);
                json.EndArray();
            }
            else if (value is bool) json.Value((bool)value);
            else if (value is long) json.Value((long)value);
            else json.Value(value as string);
        }
    }
}
