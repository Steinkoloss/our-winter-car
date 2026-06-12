using System;
using System.Collections.Generic;
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

            if (!root.TryGetValue("controls", out var controlsObj) || controlsObj is not List<object?> controls)
                return data;

            foreach (var entry in controls)
            {
                if (entry is not Dictionary<string, object?> obj) continue;
                var rule = new ControlRuleData();
                rule.PathPrefix = GetString(obj, "pathPrefix");
                rule.PathContains = GetOptionalString(obj, "pathContains");
                rule.ObjectName = GetOptionalString(obj, "objectName");
                rule.FsmName = GetString(obj, "fsmName");

                if (obj.TryGetValue("states", out var statesObj) && statesObj is List<object?> states)
                {
                    foreach (var state in states)
                    {
                        if (state is string stateName && stateName.Length > 0)
                            rule.States.Add(stateName);
                    }
                }

                if (rule.PathPrefix.Length > 0 && rule.FsmName.Length > 0 && rule.States.Count > 0)
                    data.Controls.Add(rule);
            }

            return data;
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
        public readonly List<ControlRuleData> Controls = new List<ControlRuleData>();
    }

    internal sealed class ControlRuleData
    {
        public string PathPrefix = string.Empty;
        public string? PathContains;
        public string? ObjectName;
        public string FsmName = string.Empty;
        public readonly List<string> States = new List<string>();
    }
}
