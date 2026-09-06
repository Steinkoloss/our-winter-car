using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WinterMP.Core.Catalog
{
    /// <summary>Minimal JSON reader for sync-catalog.json (objects, arrays, strings only).</summary>
    internal static partial class SyncCatalogJson
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
            if (root.TryGetValue("trophyFactories", out var factories))
                data.TrophyFactories = ParseTrophyFactories(factories);
            if (root.TryGetValue("partsPackages", out var packages))
                data.PartsPackages = ParsePartsPackages(packages);
            if (root.TryGetValue("shoppingBags", out var bags))
                data.ShoppingBags = ParseShoppingBags(bags);
            if (root.TryGetValue("partIdentity", out var partIdentity))
                data.PartIdentity = ParsePartIdentity(partIdentity);
            if (root.TryGetValue("replacementParts", out var replacements))
                data.ReplacementParts = ParseReplacementParts(replacements);
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
            if (root.TryGetValue("banking", out var bankObj) && bankObj is Dictionary<string, object?> bank)
                data.Banking = ParseBanking(bank);
            if (root.TryGetValue("vehicleDamage", out var damageObj) && damageObj is Dictionary<string, object?> damage)
                data.VehicleDamage = ParseVehicleDamage(damage);
            if (root.TryGetValue("slotMachines", out var slotsObj) && slotsObj is Dictionary<string, object?> slots)
                data.SlotMachines = ParseSlotMachines(slots);
            if (root.TryGetValue("videoPoker", out var pokerObj) && pokerObj is Dictionary<string, object?> poker)
                data.VideoPoker = ParsePoker(poker);
            if (root.TryGetValue("venttiTable", out var tableObj) && tableObj is Dictionary<string, object?> table)
                data.VenttiTable = ParseVenttiTable(table);
            if (root.TryGetValue("hockeyBetting", out var hockeyObj))
            {
                if (!(hockeyObj is Dictionary<string, object?> hockey)) throw new FormatException("Invalid hockey bindings.");
                data.HockeyBetting = ParseHockeyBetting(hockey);
            }
            if (root.TryGetValue("lottoDraw", out var lottoObj))
            {
                if (!(lottoObj is Dictionary<string, object?> lotto)) throw new FormatException("Invalid Lotto draw bindings.");
                data.LottoDraw = ParseLottoDraw(lotto);
            }
            if (root.TryGetValue("lottoTickets", out var ticketsObj) && ticketsObj is Dictionary<string, object?> tickets)
            {
                data.LottoTickets = new LottoTicketsData();
                foreach (string key in LottoTicketsData.RequiredBindings)
                    data.LottoTickets.Bindings.Add(key, RequiredString(tickets, key));
                data.LottoTickets.LinePrice = SlotNumber(tickets.TryGetValue("linePrice", out var price) ? price : null, "linePrice", 1, 1000);
                data.LottoTickets.BankThreshold = SlotNumber(tickets.TryGetValue("bankThreshold", out var threshold) ? threshold : null, "bankThreshold", 1, 1000000);
            }
            if (root.TryGetValue("rallyProgress", out var rallyObj) && rallyObj is Dictionary<string, object?> rally)
                data.RallyProgress = ParseRallyProgress(rally);
            if (root.TryGetValue("debtLetter", out var debtObj) && debtObj is Dictionary<string, object?> debt)
            {
                data.DebtLetter = new DebtLetterData();
                foreach (string key in DebtLetterData.RequiredBindings)
                    data.DebtLetter.Bindings.Add(key, RequiredString(debt, key));
            }
            if (root.TryGetValue("venttiProperty", out var propertyObj) && propertyObj is Dictionary<string, object?> property)
            {
                data.VenttiProperty = new VenttiPropertyData();
                foreach (string key in VenttiPropertyData.RequiredBindings)
                    data.VenttiProperty.Bindings.Add(key, RequiredString(property, key));
                var bindings = data.VenttiProperty;
                if (new HashSet<string> { bindings["rusckoKey"], bindings["satsumaKey"], bindings["homeKey"] }.Count != 3
                    || new HashSet<string> { bindings["sleepPath"], bindings["hatchPath"], bindings["loggingPath"] }.Count != 3)
                    throw new FormatException("Ventti property slots must have distinct bindings.");
                var tableBindings = data.VenttiTable;
                if (tableBindings != null && (bindings["managerPath"] != tableBindings["tablePath"] + "/" + tableBindings["managerPath"]
                    || bindings["managerFsm"] != tableBindings["fsm"]))
                    throw new FormatException("Ventti table and property bindings must use the same resolver.");
            }
            return data;
        }

        private static string RequiredString(Dictionary<string, object?> obj, string key)
        {
            string value = GetString(obj, key);
            if (value.Length == 0) throw new FormatException("Missing catalog binding: " + key);
            return value;
        }

        private static bool ValidScenePath(string path) => path.Length > 0 && !path.StartsWith("/", StringComparison.Ordinal)
            && !path.EndsWith("/", StringComparison.Ordinal) && path.IndexOf("//", StringComparison.Ordinal) < 0
            && path.IndexOf("..", StringComparison.Ordinal) < 0 && path.IndexOfAny(new[] { '\n', '\r', '\0', '\\' }) < 0;

        private static RallyProgressData ParseRallyProgress(Dictionary<string, object?> obj)
        {
            var data = new RallyProgressData
            {
                TimingFsm = RequiredString(obj, "timingFsm"), StartedVariable = RequiredString(obj, "startedVariable"),
                MarkerFsm = RequiredString(obj, "markerFsm"), CommitState = RequiredString(obj, "commitState"),
                CompletedState = RequiredString(obj, "completedState"),
            };
            if (data.CommitState == data.CompletedState || !obj.TryGetValue("stages", out var entries)
                || entries is not List<object?> stages || stages.Count != 3)
                throw new FormatException("Three distinct rally stage bindings are required.");
            var paths = new HashSet<string>();
            foreach (var entry in stages)
            {
                if (entry is not Dictionary<string, object?> stage || !stage.TryGetValue("checkpoints", out var pointsObj)
                    || pointsObj is not List<object?> points || points.Count < 1 || points.Count > 6)
                    throw new FormatException("Invalid rally checkpoints.");
                var binding = new RallyStageData { TimingPath = RequiredString(stage, "timingPath"),
                    StartPath = RequiredString(stage, "startPath"), Checkpoints = SlotStrings(stage, "checkpoints", points.Count) };
                if (!ValidScenePath(binding.TimingPath) || !ValidScenePath(binding.StartPath)
                    || !paths.Add(binding.TimingPath) || !paths.Add(binding.StartPath))
                    throw new FormatException("Invalid/duplicate rally stage path.");
                foreach (string checkpoint in binding.Checkpoints)
                    if (!ValidScenePath(checkpoint) || checkpoint.IndexOf('/') >= 0
                        || checkpoint == data.StartedVariable || !paths.Add(binding.TimingPath + "/" + checkpoint))
                        throw new FormatException("Invalid/duplicate rally checkpoint binding.");
                data.Stages.Add(binding);
            }
            return data;
        }

        private static BankingData ParseBanking(Dictionary<string, object?> obj)
        {
            var data = new BankingData
            {
                BankPath = RequiredString(obj, "bankPath"), BankFsm = RequiredString(obj, "bankFsm"),
                AtmPath = RequiredString(obj, "atmPath"), AtmFsm = RequiredString(obj, "atmFsm"),
                CashPath = RequiredString(obj, "cashPath"), CashFsm = RequiredString(obj, "cashFsm"),
                CashGlobal = RequiredString(obj, "cashGlobal"), BankGlobal = RequiredString(obj, "bankGlobal"),
                IncomeGlobal = RequiredString(obj, "incomeGlobal"),
            };
            if (!obj.TryGetValue("mutations", out var entries) || entries is not List<object?> mutations)
                throw new FormatException("Banking mutations are required.");
            foreach (var entry in mutations)
            {
                if (entry is not Dictionary<string, object?> mutation)
                    throw new FormatException("Invalid banking mutation.");
                var binding = new BankMutationData
                {
                    Target = RequiredString(mutation, "target"), State = RequiredString(mutation, "state"),
                    ActionType = RequiredString(mutation, "actionType"),
                    Balance = RequiredString(mutation, "balance"),
                    AmountVariable = GetString(mutation, "amountVariable"),
                    Direction = GetFloat(mutation, "direction", 0f),
                };
                if ((binding.Target != "atm" && binding.Target != "cash")
                    || (binding.Balance != "cash" && binding.Balance != "bank")
                    || (binding.Direction != 0 && binding.Direction != 1 && binding.Direction != -1)
                    || (binding.Direction != 0 && binding.AmountVariable.Length == 0))
                    throw new FormatException("Invalid banking transfer binding.");
                foreach (var previous in data.Mutations)
                    if (previous.Target == binding.Target && previous.State == binding.State)
                        throw new FormatException("Duplicate banking mutation state.");
                data.Mutations.Add(binding);
            }
            if (data.Mutations.Count == 0) throw new FormatException("Banking mutations cannot be empty.");
            return data;
        }

        private static VehicleDamageData ParseVehicleDamage(Dictionary<string, object?> obj)
        {
            var data = new VehicleDamageData
            {
                ObjectName = RequiredString(obj, "objectName"), FsmName = RequiredString(obj, "fsmName"),
                IdleState = RequiredString(obj, "idleState"), PartFsmName = RequiredString(obj, "partFsmName"),
                WearVariable = RequiredString(obj, "wearVariable"),
            };
            ReadDamageSlots(obj, "events", data.Events);
            ReadDamageSlots(obj, "partVariables", data.PartVariables);
            if (data.Events.Count != 16 || data.PartVariables.Count != 16)
                throw new FormatException("Vehicle damage requires 16 fixed protocol slots.");
            for (int i = 0; i < 16; i++)
            {
                bool retired = i == 13 || i == 15;
                if ((data.Events[i].Length == 0) != retired || (data.PartVariables[i].Length == 0) != retired)
                    throw new FormatException("Vehicle damage slots 13/15 are retired; concrete slots require bindings.");
                if (data.Events[i] == "SEIZE" || data.Events[i] == "CAMFAIL")
                    throw new FormatException("Random damage selectors cannot be replay events.");
            }
            return data;
        }

        private static void ReadDamageSlots(Dictionary<string, object?> obj, string key, List<string> target)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> slots)
                throw new FormatException("Missing damage slots: " + key);
            foreach (var slot in slots)
            {
                if (slot is not string text) throw new FormatException("Damage slots must be strings.");
                // Empty retired slots must retain their protocol bit positions.
                target.Add(text);
            }
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
                ScalarFloatName = GetOptionalString(obj, "scalarFloat"),
                ScalarCommitState = GetOptionalString(obj, "scalarCommitState"),
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
        public BankingData? Banking;
        public VehicleDamageData? VehicleDamage;
        public SlotMachineData? SlotMachines;
        public PokerData? VideoPoker;
        public DebtLetterData? DebtLetter;
        public VenttiPropertyData? VenttiProperty;
        public VenttiTableData? VenttiTable;
        public RallyProgressData? RallyProgress;
        public LottoDrawData? LottoDraw;
        public HockeyBettingData? HockeyBetting;
        public LottoTicketsData? LottoTickets;
        public TrophyFactoriesData? TrophyFactories;
        public PartsPackagesData? PartsPackages;
        public ShoppingBagsData? ShoppingBags;
        public PartIdentityData? PartIdentity;
        public ReplacementPartsData? ReplacementParts;
    }

    internal sealed class VenttiPropertyData
    {
        public static readonly string[] RequiredBindings = {
            "managerPath", "managerFsm", "rusckoKey", "satsumaKey", "homeKey",
            "sleepPath", "hatchPath", "loggingPath", "loggingFsm",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }

    internal sealed class DebtLetterData
    {
        public static readonly string[] RequiredBindings = {
            "rentPath", "rentFsm", "rentDebt", "rentEnvelope", "sheetPath", "setupFsm", "calculateState",
            "calculatedDebt", "originalText", "totalText", "interest", "cost1", "cost2",
            "payPath", "payFsm", "requestState", "commitState", "idleState", "fundsState", "closeState",
            "payTotal", "payDatabase", "payEnvelope", "paySheet",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
    }

    internal sealed class RallyProgressData
    {
        public string TimingFsm = "", StartedVariable = "", MarkerFsm = "", CommitState = "", CompletedState = "";
        public readonly List<RallyStageData> Stages = new List<RallyStageData>();
    }
    internal sealed class RallyStageData
    {
        public string TimingPath = "", StartPath = "";
        public string[] Checkpoints = new string[0];
    }

    internal sealed class BankingData
    {
        public string BankPath = string.Empty, BankFsm = string.Empty;
        public string AtmPath = string.Empty, AtmFsm = string.Empty;
        public string CashPath = string.Empty, CashFsm = string.Empty;
        public string CashGlobal = string.Empty, BankGlobal = string.Empty, IncomeGlobal = string.Empty;
        public readonly List<BankMutationData> Mutations = new List<BankMutationData>();
    }

    internal sealed class BankMutationData
    {
        public string Target = string.Empty, State = string.Empty, ActionType = string.Empty;
        public string AmountVariable = string.Empty, Balance = string.Empty;
        public float Direction;
    }

    internal sealed class VehicleDamageData
    {
        public string ObjectName = string.Empty, FsmName = string.Empty, IdleState = string.Empty;
        public string PartFsmName = string.Empty, WearVariable = string.Empty;
        public readonly List<string> Events = new List<string>();
        public readonly List<string> PartVariables = new List<string>();
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
        public string? ScalarFloatName;
        public string? ScalarCommitState;
        public readonly List<string> States = new List<string>();
        public readonly List<string> RequireStates = new List<string>();
        public readonly List<string> ExcludePathPrefixes = new List<string>();
    }
}
