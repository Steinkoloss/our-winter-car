using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinterMP.Core.Catalog
{
    /// <summary>Minimal JSON reader for sync-catalog.json (objects, arrays, strings only).</summary>
    internal static class SyncCatalogJson
    {
        public static SyncCatalogData Parse(string json)
        {
            var reader = new Reader(json ?? string.Empty);
            reader.SkipWhitespace();
            var root = reader.ReadObject();
            return ToData(root);
        }

        private static SyncCatalogData ToData(Dictionary<string, object?> root)
        {
            var data = new SyncCatalogData();
            if (root.TryGetValue("gameBuild", out var build) && build is string buildText)
                data.GameBuild = buildText;

            ParseRuleArray(root, "doors", data.Doors);
            ParseRuleArray(root, "spawnContainers", data.SpawnContainers);
            ParseRuleArray(root, "controls", data.Controls);
            ParseRuleArray(root, "switchRules", data.SwitchRules);
            ParseRuleArray(root, "ignitions", data.Ignitions);
            ParseRuleArray(root, "starters", data.Starters);
            ParseBuyArray(root, "buys", data.Buys);
            ParsePartArray(root, "parts", data.Parts);
            ParseBoltArray(root, "bolts", data.Bolts);
            if (root.TryGetValue("vehicles", out var vehiclesObj) && vehiclesObj is Dictionary<string, object?> vehicles)
                data.Vehicles = ParseVehicles(vehicles);
            if (root.TryGetValue("pickables", out var pickablesObj) && pickablesObj is Dictionary<string, object?> pickables)
                data.Pickables = ParsePickables(pickables);
            if (root.TryGetValue("consumables", out var consumablesObj) && consumablesObj is Dictionary<string, object?> consumables)
                data.Consumables = ParseConsumables(consumables);
            if (root.TryGetValue("vehicleClimate", out var climateObj) && climateObj is Dictionary<string, object?> climate)
                data.VehicleClimate = ParseVehicleClimate(climate);
            return data;
        }

        private static VehicleRegistrationData ParseVehicles(Dictionary<string, object?> obj)
        {
            var data = new VehicleRegistrationData
            {
                MinMass = GetFloat(obj, "minMass", 150f),
                RequireRoot = GetBool(obj, "requireRoot", true),
            };
            AppendStrings(obj, "namePrefixes", data.NamePrefixes);
            return data;
        }

        private static PickableRegistrationData ParsePickables(Dictionary<string, object?> obj)
        {
            var data = new PickableRegistrationData
            {
                ProbeUseFsm = GetBool(obj, "probeUseFsm", true),
            };
            AppendStrings(obj, "excludeNameContains", data.ExcludeNameContains);
            AppendStrings(obj, "nameSuffixes", data.NameSuffixes);
            return data;
        }

        private static ConsumableData ParseConsumables(Dictionary<string, object?> obj)
        {
            var data = new ConsumableData
            {
                FsmName = GetString(obj, "fsmName"),
                DrinkCheckState = GetString(obj, "drinkCheckState"),
            };
            if (data.FsmName.Length == 0) data.FsmName = "Use";
            if (data.DrinkCheckState.Length == 0) data.DrinkCheckState = "Check drink";
            AppendStrings(obj, "destroyStates", data.DestroyStates);
            AppendStrings(obj, "drinkEmptyStates", data.DrinkEmptyStates);
            return data;
        }

        private static VehicleClimateData ParseVehicleClimate(Dictionary<string, object?> obj)
        {
            var data = new VehicleClimateData();
            AppendStrings(obj, "pathPrefixes", data.PathPrefixes);
            AppendStrings(obj, "carTempPathContains", data.CarTempPathContains);
            AppendStrings(obj, "heaterPathContains", data.HeaterPathContains);
            return data;
        }

        private static float GetFloat(Dictionary<string, object?> obj, string key, float defaultValue)
        {
            if (!obj.TryGetValue(key, out var value) || value == null) return defaultValue;
            if (value is double d) return (float)d;
            if (value is long l) return l;
            return defaultValue;
        }

        private static bool GetBool(Dictionary<string, object?> obj, string key, bool defaultValue)
        {
            if (!obj.TryGetValue(key, out var value)) return defaultValue;
            return value is bool b ? b : defaultValue;
        }

        private static void ParseBuyArray(
            Dictionary<string, object?> root,
            string key,
            List<BuyRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParseBuyRule(obj);
                if (rule.FsmName.Length == 0) continue;
                if (rule.Template == "shopBuy")
                {
                    target.Add(rule);
                    continue;
                }

                if (rule.EntryGuards.Count > 0 && rule.ResultStates.Count > 0)
                    target.Add(rule);
            }
        }

        private static void ParsePartArray(
            Dictionary<string, object?> root,
            string key,
            List<PartRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParsePartRule(obj);
                if (rule.FsmName.Length > 0 && rule.States.Count > 0)
                    target.Add(rule);
            }
        }

        private static PartRuleData ParsePartRule(Dictionary<string, object?> obj)
        {
            var rule = new PartRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
            };

            AppendStrings(obj, "states", rule.States);
            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "optionalStates", rule.OptionalStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);
            return rule;
        }

        private static void ParseBoltArray(
            Dictionary<string, object?> root,
            string key,
            List<BoltRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParseBoltRule(obj);
                if (rule.FsmName.Length > 0 && rule.RequireStates.Count > 0)
                    target.Add(rule);
            }
        }

        private static BoltRuleData ParseBoltRule(Dictionary<string, object?> obj)
        {
            var rule = new BoltRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
            };

            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);
            return rule;
        }

        private static BuyRuleData ParseBuyRule(Dictionary<string, object?> obj)
        {
            var rule = new BuyRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
                Template = GetOptionalString(obj, "template") ?? string.Empty,
            };

            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "resultStates", rule.ResultStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);

            if (!obj.TryGetValue("entryGuards", out var guardsObj) || guardsObj is not List<object?> guards)
                return rule;

            foreach (var guardObj in guards)
            {
                if (guardObj is not Dictionary<string, object?> guardDict) continue;
                string state = GetString(guardDict, "state");
                string trigger = GetString(guardDict, "event");
                if (state.Length == 0 || trigger.Length == 0) continue;
                rule.EntryGuards.Add(new BuyGuardData
                {
                    StateName = state,
                    TriggerEvent = trigger,
                    Optional = GetBool(guardDict, "optional"),
                });
            }

            return rule;
        }

        private static bool GetBool(Dictionary<string, object?> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value)) return false;
            return value is bool b && b;
        }

        private static void ParseRuleArray(
            Dictionary<string, object?> root,
            string key,
            List<CatalogRuleData> target)
        {
            if (!root.TryGetValue(key, out var arrayObj) || arrayObj is not List<object?> entries)
                return;

            foreach (var entry in entries)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = ParseRule(obj);
                if (rule.FsmName.Length > 0 && rule.States.Count > 0)
                    target.Add(rule);
            }
        }

        private static CatalogRuleData ParseRule(Dictionary<string, object?> obj)
        {
            var rule = new CatalogRuleData
            {
                PathPrefix = GetString(obj, "pathPrefix"),
                PathContains = GetOptionalString(obj, "pathContains"),
                ObjectName = GetOptionalString(obj, "objectName"),
                ObjectNameContains = GetOptionalString(obj, "objectNameContains"),
                FsmName = GetString(obj, "fsmName"),
            };

            AppendStrings(obj, "states", rule.States);
            AppendStrings(obj, "requireStates", rule.RequireStates);
            AppendStrings(obj, "excludePathPrefixes", rule.ExcludePathPrefixes);
            return rule;
        }

        private static void AppendStrings(Dictionary<string, object?> obj, string key, List<string> target)
        {
            if (!obj.TryGetValue(key, out var valuesObj) || valuesObj is not List<object?> values)
                return;

            foreach (var value in values)
            {
                if (value is string text && text.Length > 0)
                    target.Add(text);
            }
        }

        private static string GetString(Dictionary<string, object?> obj, string key)
        {
            return obj.TryGetValue(key, out var value) && value is string text ? text : string.Empty;
        }

        private static string? GetOptionalString(Dictionary<string, object?> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || value is not string text || text.Length == 0)
                return null;
            return text;
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _index;

            internal Reader(string text)
            {
                _text = text;
            }

            internal void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                    _index++;
            }

            internal Dictionary<string, object?> ReadObject()
            {
                Expect('{');
                var obj = new Dictionary<string, object?>();
                SkipWhitespace();
                if (TryConsume('}')) return obj;

                while (true)
                {
                    SkipWhitespace();
                    string key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    obj[key] = ReadValue();
                    SkipWhitespace();
                    if (TryConsume('}')) break;
                    Expect(',');
                }

                return obj;
            }

            internal List<object?> ReadArray()
            {
                Expect('[');
                var list = new List<object?>();
                SkipWhitespace();
                if (TryConsume(']')) return list;

                while (true)
                {
                    SkipWhitespace();
                    list.Add(ReadValue());
                    SkipWhitespace();
                    if (TryConsume(']')) break;
                    Expect(',');
                }

                return list;
            }

            private object? ReadValue()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                    throw new FormatException("Unexpected end of JSON.");

                char c = _text[_index];
                if (c == '"') return ReadString();
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == 'n' && MatchLiteral("null")) return null;
                if (c == 't' && MatchLiteral("true")) return true;
                if (c == 'f' && MatchLiteral("false")) return false;
                if (c == '-' || char.IsDigit(c)) return ReadNumber();
                throw new FormatException("Unsupported JSON value at " + _index + ".");
            }

            internal string ReadString()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (_index < _text.Length)
                {
                    char c = _text[_index++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (_index >= _text.Length)
                            throw new FormatException("Unterminated escape.");
                        char esc = _text[_index++];
                        switch (esc)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (_index + 4 > _text.Length)
                                    throw new FormatException("Invalid unicode escape.");
                                sb.Append((char)Convert.ToInt32(_text.Substring(_index, 4), 16));
                                _index += 4;
                                break;
                            default: throw new FormatException("Invalid escape.");
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                throw new FormatException("Unterminated string.");
            }

            private object ReadNumber()
            {
                int start = _index;
                if (_text[_index] == '-') _index++;
                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;
                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                        _index++;
                    return double.Parse(_text.Substring(start, _index - start), CultureInfo.InvariantCulture);
                }

                return long.Parse(_text.Substring(start, _index - start), CultureInfo.InvariantCulture);
            }

            private bool MatchLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length) return false;
                if (string.Compare(_text, _index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    return false;
                _index += literal.Length;
                return true;
            }

            private void Expect(char ch)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != ch)
                    throw new FormatException("Expected '" + ch + "' at " + _index + ".");
                _index++;
            }

            private bool TryConsume(char ch)
            {
                if (_index >= _text.Length || _text[_index] != ch) return false;
                _index++;
                return true;
            }
        }
    }

    internal sealed class SyncCatalogData
    {
        public string? GameBuild;
        public readonly List<CatalogRuleData> Doors = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> SpawnContainers = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> Controls = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> SwitchRules = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> Ignitions = new List<CatalogRuleData>();
        public readonly List<CatalogRuleData> Starters = new List<CatalogRuleData>();
        public readonly List<BuyRuleData> Buys = new List<BuyRuleData>();
        public readonly List<PartRuleData> Parts = new List<PartRuleData>();
        public readonly List<BoltRuleData> Bolts = new List<BoltRuleData>();
        public VehicleRegistrationData? Vehicles;
        public PickableRegistrationData? Pickables;
        public ConsumableData? Consumables;
        public VehicleClimateData? VehicleClimate;
    }

    internal sealed class VehicleRegistrationData
    {
        public float MinMass = 150f;
        public bool RequireRoot = true;
        public readonly List<string> NamePrefixes = new List<string>();
    }

    internal sealed class PickableRegistrationData
    {
        public bool ProbeUseFsm = true;
        public readonly List<string> ExcludeNameContains = new List<string>();
        public readonly List<string> NameSuffixes = new List<string>();
    }

    internal sealed class ConsumableData
    {
        public string FsmName = "Use";
        public string DrinkCheckState = "Check drink";
        public readonly List<string> DestroyStates = new List<string>();
        public readonly List<string> DrinkEmptyStates = new List<string>();
    }

    internal sealed class VehicleClimateData
    {
        public readonly List<string> PathPrefixes = new List<string>();
        public readonly List<string> CarTempPathContains = new List<string>();
        public readonly List<string> HeaterPathContains = new List<string>();
    }

    internal sealed class BuyRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public string Template = string.Empty;
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ResultStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
        public readonly List<BuyGuardData> EntryGuards = new List<BuyGuardData>();
    }

    internal sealed class BuyGuardData
    {
        public string StateName = string.Empty;
        public string TriggerEvent = string.Empty;
        public bool Optional;
    }

    internal sealed class PartRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public readonly List<string> States = new List<string>();
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> OptionalStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }

    internal sealed class BoltRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }

    internal sealed class CatalogRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string? ObjectNameContains;
        public string FsmName = string.Empty;
        public readonly List<string> States = new List<string>();
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }
}
