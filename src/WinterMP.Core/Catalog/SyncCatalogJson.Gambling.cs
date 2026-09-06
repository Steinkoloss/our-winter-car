using System;
using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
        private static HockeyBettingData ParseHockeyBetting(Dictionary<string, object?> obj)
        {
            var data = new HockeyBettingData();
            foreach (string key in HockeyBettingData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            data.Ints = SlotStrings(obj, "ints", 4); data.Floats = SlotStrings(obj, "floats", 3);
            data.Lists = SlotStrings(obj, "lists", 9); data.Tables = SlotStrings(obj, "tables", 6);
            data.Keys = SlotStrings(obj, "keys", 3); data.BettingIdle = SlotStrings(obj, "bettingIdle", 2);
            data.SeasonIdle = SlotStrings(obj, "seasonIdle", 3); data.Displays = SlotStrings(obj, "displays", 3);
            foreach (var names in new[] { data.Ints, data.Floats, data.Lists, data.Tables, data.Keys,
                data.BettingIdle, data.SeasonIdle, data.Displays })
                if (new HashSet<string>(names).Count != names.Length) throw new FormatException("Duplicate hockey binding.");
            if (!ValidScenePath(data["bettingPath"]) || !ValidScenePath(data["seasonPath"])
                || data["bettingPath"] == data["seasonPath"]) throw new FormatException("Invalid hockey source paths.");
            foreach (string path in data.Displays) if (!ValidScenePath(path)) throw new FormatException("Invalid hockey display path.");
            return data;
        }

        private static LottoDrawData ParseLottoDraw(Dictionary<string, object?> obj)
        {
            var data = new LottoDrawData
            {
                Path = RequiredString(obj, "path"), Fsm = RequiredString(obj, "fsm"),
                DrawDone = RequiredString(obj, "drawDone"), ResultsPath = RequiredString(obj, "resultsPath"),
                Scalars = SlotStrings(obj, "scalars", 5), WinnerVariables = SlotStrings(obj, "winnerVariables", LottoDrawState.TierCount),
                Lists = SlotStrings(obj, "lists", 4), StableStates = SlotStrings(obj, "stableStates", 4),
            };
            var variables = new HashSet<string>(data.Scalars);
            foreach (string variable in data.WinnerVariables)
                if (!variables.Add(variable)) throw new FormatException("Duplicate Lotto variable binding.");
            if (variables.Count != 10 || !variables.Add(data.DrawDone)
                || new HashSet<string>(data.Lists).Count != 4 || new HashSet<string>(data.StableStates).Count != 4
                || !ValidScenePath(data.Path) || !ValidScenePath(data.ResultsPath) || data.Path == data.ResultsPath)
                throw new FormatException("Invalid/duplicate Lotto binding.");
            return data;
        }

        private static VenttiTableData ParseVenttiTable(Dictionary<string, object?> obj)
        {
            var data = new VenttiTableData();
            foreach (string key in VenttiTableData.RequiredBindings)
                data.Bindings.Add(key, RequiredString(obj, key));
            var texts = new HashSet<string>();
            foreach (string key in VenttiTableData.OutcomeBindings)
                if (!texts.Add(data[key])) throw new FormatException("Ventti result texts must be distinct.");
            if (new HashSet<string> { data["betPath"], data["playerPath"], data["housePath"], data["standPath"], data["managerPath"] }.Count != 5)
                throw new FormatException("Ventti table FSMs must have distinct paths.");
            if (obj.TryGetValue("rules", out var rulesObj))
            {
                if (rulesObj is not Dictionary<string, object?> rules) throw new FormatException("Invalid Ventti rules.");
                try
                {
                    data.Rules = new VenttiRules(SlotNumbers(rules, "cardValues", 53, 0, 13),
                        VenttiNumber(rules, "betIncrement"), VenttiNumber(rules, "propertyThreshold"),
                        VenttiNumber(rules, "winLimitIncrease"), VenttiNumber(rules, "opponentLossLimit"),
                        VenttiNumber(rules, "propertyWinLoss"), VenttiNumber(rules, "propertyLoseLoss"));
                }
                catch (ArgumentException e) { throw new FormatException("Invalid Ventti rules: " + e.Message, e); }
            }
            data.IdleStates = SlotStrings(obj, "idleStates", 2);
            obj.TryGetValue("materialIndex", out var materialIndex);
            obj.TryGetValue("cardSlots", out var cardSlots);
            data.MaterialIndex = SlotNumber(materialIndex, "materialIndex", 0, 8);
            data.CardSlots = SlotNumber(cardSlots, "cardSlots", 2, 21);
            if (data.IdleStates[0] == data.IdleStates[1]) throw new FormatException("Duplicate Ventti idle state.");
            data.PickDistance = VenttiNumber(obj, "pickDistance");
            data.WinStress = VenttiNumber(obj, "winStress");
            data.LoseStress = VenttiNumber(obj, "loseStress");
            if (!(data.PickDistance > 0 && data.PickDistance <= 6)
                || !BankTransferPolicy.IsFinite(data.WinStress) || data.WinStress < 0
                || !BankTransferPolicy.IsFinite(data.LoseStress) || data.LoseStress < 0)
                throw new FormatException("Invalid Ventti interaction settings.");
            if (!obj.TryGetValue("outcomes", out var effectsObj) || effectsObj is not List<object?> effects || effects.Count != 6)
                throw new FormatException("Six Ventti outcome bindings are required.");
            var states = new HashSet<string>();
            foreach (var entry in effects)
            {
                if (entry is not Dictionary<string, object?> effect
                    || !effect.TryGetValue("actionTypes", out var typesObj) || typesObj is not List<object?> types || types.Count == 0
                    || !effect.TryGetValue("suppressActions", out var cutsObj) || cutsObj is not List<object?> cuts || cuts.Count == 0)
                    throw new FormatException("Invalid Ventti outcome actions.");
                var binding = new VenttiOutcomeData
                {
                    State = RequiredString(effect, "state"), ActionTypes = SlotStrings(effect, "actionTypes", types.Count),
                    SuppressActions = SlotNumbers(effect, "suppressActions", cuts.Count, 0, types.Count - 1),
                };
                if (!states.Add(binding.State) || new HashSet<int>(binding.SuppressActions).Count != cuts.Count)
                    throw new FormatException("Duplicate Ventti outcome binding.");
                data.Outcomes.Add(binding);
            }
            if (obj.TryGetValue("reactions", out var reactions))
            {
                if (reactions is not Dictionary<string, object?> reactionObj) throw new FormatException("Invalid Ventti reactions.");
                data.Reactions = ParseVenttiReactions(reactionObj);
                if (data.Reactions.RootPath + "/" + data.Reactions.Poses[1] != data["tablePath"])
                    throw new FormatException("Ventti reaction table must match the game table.");
            }
            return data;
        }

        private static VenttiReactionData ParseVenttiReactions(Dictionary<string, object?> obj)
        {
            var data = new VenttiReactionData { RootPath = RequiredString(obj, "rootPath") };
            if (!ValidScenePath(data.RootPath)) throw new FormatException("Invalid Ventti reaction root.");
            if (!obj.TryGetValue("poses", out var posesObj) || posesObj is not List<object?> poses
                || poses.Count < VenttiSceneState.WorldPoseCount || poses.Count > VenttiSceneState.MaxPoses
                || !obj.TryGetValue("sounds", out var soundsObj) || soundsObj is not List<object?> sounds || sounds.Count < 1 || sounds.Count > 256)
                throw new FormatException("Invalid Ventti reaction slots.");
            data.Poses = SlotStrings(obj, "poses", poses.Count);
            data.Sounds = SlotStrings(obj, "sounds", sounds.Count);
            for (int i = 0; i < VenttiSceneState.WorldPoseCount; i++)
                for (int j = 0; j < VenttiSceneState.WorldPoseCount; j++)
                    if (i != j && data.Poses[i].StartsWith(data.Poses[j] + "/", StringComparison.Ordinal))
                        throw new FormatException("Ventti world pose roots must not overlap.");
            var known = new HashSet<string>();
            foreach (string path in data.Poses)
            {
                if (!ValidScenePath(path) || !known.Add(path)) throw new FormatException("Invalid/duplicate Ventti pose path.");
                if (known.Count > VenttiSceneState.WorldPoseCount)
                {
                    int slash = path.LastIndexOf('/');
                    if (!path.StartsWith(data.Poses[0] + "/", StringComparison.Ordinal) || slash < 0 || !known.Contains(path.Substring(0, slash)))
                        throw new FormatException("Ventti local poses must follow their NPC parents.");
                }
            }
            if (new HashSet<string>(data.Sounds).Count != sounds.Count) throw new FormatException("Duplicate Ventti sound.");
            foreach (string path in data.Sounds)
                if (!ValidScenePath(path) || !path.StartsWith("MasterAudio/", StringComparison.Ordinal)
                    || path.Split('/').Length != 3) throw new FormatException("Invalid Ventti sound source path.");
            if (!obj.TryGetValue("sources", out var sourcesObj) || sourcesObj is not List<object?> sources || sources.Count == 0)
                throw new FormatException("Missing Ventti native sound hooks.");
            known.Clear();
            foreach (var entry in sources)
            {
                if (entry is not Dictionary<string, object?> source) throw new FormatException("Invalid Ventti sound hook.");
                source.TryGetValue("actionIndex", out var index);
                var binding = new VenttiSoundSourceData
                {
                    Path = RequiredString(source, "path"), Fsm = RequiredString(source, "fsm"), State = RequiredString(source, "state"),
                    Origin = RequiredString(source, "origin"), Group = RequiredString(source, "group"),
                    ActionIndex = SlotNumber(index, "actionIndex", 0, 255), Delay = VenttiNumber(source, "delay"),
                    Variable = GetString(source, "variable"), Variation = GetString(source, "variation"),
                };
                if (!ValidScenePath(binding.Path) || !ValidScenePath(binding.Origin)
                    || (binding.Variable.Length == 0) == (binding.Variation.Length == 0)
                    || !BankTransferPolicy.IsFinite(binding.Delay) || binding.Delay < 0 || binding.Delay > 5
                    || !known.Add(binding.Path + "::" + binding.Fsm + "::" + binding.State + "::" + binding.ActionIndex))
                    throw new FormatException("Invalid/duplicate Ventti sound hook.");
                if (binding.Variation.Length != 0 && Array.IndexOf(data.Sounds, "MasterAudio/" + binding.Group + "/" + binding.Variation) < 0)
                    throw new FormatException("Ventti sound hook names an unlisted sound.");
                if (!Array.Exists(data.Sounds, path => path.StartsWith("MasterAudio/" + binding.Group + "/", StringComparison.Ordinal)))
                    throw new FormatException("Ventti sound hook names an unlisted group.");
                data.Sources.Add(binding);
            }
            data.LayoutId = VenttiSceneReplica.Layout(data.RootPath, data.Poses, data.Sounds);
            return data;
        }

        private static float VenttiNumber(Dictionary<string, object?> obj, string key)
        {
            if (!obj.TryGetValue(key, out var value) || (value is not long && value is not double))
                throw new FormatException("Missing numeric Ventti rule: " + key);
            return GetFloat(obj, key, float.NaN);
        }

        private static PokerData ParsePoker(Dictionary<string, object?> obj)
        {
            var data = new PokerData();
            foreach (string key in PokerData.RequiredBindings) data.Bindings.Add(key, RequiredString(obj, key));
            data.Buttons = SlotStrings(obj, "buttons", 12);
            if (new HashSet<string>(data.Buttons).Count != 12) throw new FormatException("Duplicate poker control.");
            data.Cards = SlotStrings(obj, "cards", 5);
            data.Backs = SlotStrings(obj, "backs", 5);
            data.ButtonMeshes = SlotStrings(obj, "buttonMeshes", 11);
            data.IdleStates = SlotStrings(obj, "idleStates", 6);
            data.Payouts = SlotNumbers(obj, "payouts", 10, 0, 50);
            if (data.Payouts[0] != 0) throw new FormatException("A losing poker hand cannot pay.");
            obj.TryGetValue("achievementThreshold", out var threshold);
            data.AchievementThreshold = SlotNumber(threshold, "achievementThreshold", 1, 9999);
            data.HeldZ = GetFloat(obj, "heldZ", -3.5f); data.RestZ = GetFloat(obj, "restZ", -4.2f);
            data.MarkerX = GetFloat(obj, "markerX", -2.3f); data.MarkerStartZ = GetFloat(obj, "markerStartZ", -1f);
            data.MarkerStepZ = GetFloat(obj, "markerStepZ", .75f);
            if (!obj.TryGetValue("assets", out var entries) || entries is not List<object?> assets)
                throw new FormatException("Poker visual sources are required.");
            foreach (var entry in assets)
            {
                if (entry is not Dictionary<string, object?> asset) throw new FormatException("Invalid poker asset.");
                var source = new PokerAssetData
                {
                    Key = RequiredString(asset, "key"), Path = RequiredString(asset, "path"),
                    Fsm = RequiredString(asset, "fsm"), State = RequiredString(asset, "state"),
                    Action = RequiredString(asset, "action"), Field = RequiredString(asset, "field"),
                };
                if (data.Assets.ContainsKey(source.Key)) throw new FormatException("Duplicate poker asset key.");
                data.Assets.Add(source.Key, source);
            }
            foreach (string key in PokerData.RequiredAssets)
                if (!data.Assets.ContainsKey(key)) throw new FormatException("Missing poker asset: " + key);
            for (int i = 0; i < 4; i++) RequirePokerAsset(data, "suit" + i);
            for (int i = 1; i <= 5; i++) RequirePokerAsset(data, "bet" + i);
            for (int i = 1; i <= 13; i++) { RequirePokerAsset(data, "black" + i); RequirePokerAsset(data, "red" + i); }
            return data;
        }

        private static void RequirePokerAsset(PokerData data, string key)
        {
            if (!data.Assets.ContainsKey(key)) throw new FormatException("Missing poker asset: " + key);
        }

        private static SlotMachineData ParseSlotMachines(Dictionary<string, object?> obj)
        {
            var data = new SlotMachineData
            {
                FsmName = RequiredString(obj, "fsmName"), IdleState = RequiredString(obj, "idleState"),
                SpinState = RequiredString(obj, "spinState"), FinishState = RequiredString(obj, "finishState"),
                CreditVariable = RequiredString(obj, "creditVariable"), WinningsVariable = RequiredString(obj, "winningsVariable"),
                LastWinVariable = RequiredString(obj, "lastWinVariable"), BetVariable = RequiredString(obj, "betVariable"),
                BetButtonVariable = RequiredString(obj, "betButtonVariable"), CanHoldVariable = RequiredString(obj, "canHoldVariable"),
                RollVariable = RequiredString(obj, "rollVariable"), RandomAction = RequiredString(obj, "randomAction"),
                SoundPath = RequiredString(obj, "soundPath"), HoldSwitchVariable = RequiredString(obj, "holdSwitchVariable"),
                StatsPath = RequiredString(obj, "statsPath"), StatsFsm = RequiredString(obj, "statsFsm"),
                StatsMoneyIn = RequiredString(obj, "statsMoneyIn"), StatsMoneyOut = RequiredString(obj, "statsMoneyOut"),
                AchievementObjectGlobal = RequiredString(obj, "achievementObjectGlobal"),
                AchievementFsm = RequiredString(obj, "achievementFsm"), AchievementEvent = RequiredString(obj, "achievementEvent"),
            };
            AppendStrings(obj, "paths", data.Paths);
            if (data.Paths.Count == 0 || new HashSet<string>(data.Paths).Count != data.Paths.Count)
                throw new FormatException("Slot machine paths must be nonempty and unique.");
            data.ButtonPaths = SlotStrings(obj, "buttonPaths", 7);
            data.ButtonStates = SlotStrings(obj, "buttonStates", 7);
            if (new HashSet<string>(data.ButtonPaths).Count != 7)
                throw new FormatException("Slot button paths must be unique.");
            data.RollStates = SlotStrings(obj, "rollStates", 3);
            data.ReelVariables = SlotStrings(obj, "reelVariables", 3);
            data.ReelObjectVariables = SlotStrings(obj, "reelObjectVariables", 3);
            data.HoldVariables = SlotStrings(obj, "holdVariables", 3);
            data.HoldStates = SlotStrings(obj, "holdStates", 2);
            data.TextPaths = SlotStrings(obj, "textPaths", 4);
            data.Reels = new[] { SlotNumbers(obj, "reel1", 24, 1, 9), SlotNumbers(obj, "reel2", 24, 1, 9), SlotNumbers(obj, "reel3", 24, 1, 9) };
            data.Payouts = SlotNumbers(obj, "payoutMultipliers", 10, 0, 1000);
            obj.TryGetValue("achievementThreshold", out var threshold);
            data.AchievementThreshold = SlotNumber(threshold, "achievementThreshold", 1, 1000000);
            return data;
        }

        private static string[] SlotStrings(Dictionary<string, object?> obj, string key, int count)
        {
            var values = new List<string>();
            ReadDamageSlots(obj, key, values);
            if (values.Count != count || values.Contains(string.Empty))
                throw new FormatException("Invalid slot bindings: " + key);
            return values.ToArray();
        }

        private static int[] SlotNumbers(Dictionary<string, object?> obj, string key, int count, int min, int max)
        {
            if (!obj.TryGetValue(key, out var value) || value is not List<object?> values || values.Count != count)
                throw new FormatException("Invalid slot rules: " + key);
            var numbers = new int[count];
            for (int i = 0; i < count; i++)
                numbers[i] = SlotNumber(values[i], key, min, max);
            return numbers;
        }

        private static int SlotNumber(object? value, string key, int min, int max)
        {
            double number = value is long l ? l : value is double d ? d : double.NaN;
            if (double.IsNaN(number) || number < min || number > max || number != (int)number)
                throw new FormatException("Invalid slot rule value: " + key);
            return (int)number;
        }
    }

    internal sealed class HockeyBettingData
    {
        public static readonly string[] RequiredBindings = { "bettingPath", "bettingFsm", "seasonPath", "seasonFsm",
            "result", "gamesPlayed", "kurpaWins" };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public string this[string key] => Bindings[key];
        public string[] Ints = new string[0], Floats = new string[0], Lists = new string[0], Tables = new string[0],
            Keys = new string[0], BettingIdle = new string[0], SeasonIdle = new string[0], Displays = new string[0];
    }

    internal sealed class LottoTicketsData
    {
        public static readonly string[] RequiredBindings = {
            "payPath", "payFsm", "requestState", "commitState", "closeState", "fundsState", "payTotal", "payRound",
            "spawnerPath", "spawnerFsm", "spawnerIdle", "prefab", "spawnPoint", "counter", "saveId", "database", "paper",
            "useFsm", "dataFsm", "ticketId", "ticketRound", "winnings", "ticketDatabase", "ticketPaper", "ticketName",
            "line1", "line2", "line3", "claimPath", "claimFsm", "claimState", "claimReset", "claimObject",
            "destroyState", "loadState", "saveState", "deleteState", "useIdle", "dataIdle",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public int LinePrice, BankThreshold;
        public string this[string key] => Bindings[key];
    }

    internal sealed class LottoDrawData
    {
        public string Path = "", Fsm = "", DrawDone = "", ResultsPath = "";
        // Wire slot order is fixed; a catalog update may rename bindings, never reorder their meaning.
        public string[] Scalars = new string[0], WinnerVariables = new string[0], Lists = new string[0], StableStates = new string[0];
    }

    internal sealed class VenttiTableData
    {
        public static readonly string[] RequiredBindings = {
            "tablePath", "betPath", "playerPath", "housePath", "standPath", "managerPath", "fsm", "resultFsm",
            "stake", "playerHand", "houseHand", "result", "wagerCar", "wagerHouse",
            "winText", "loseText", "winCarText", "loseCarText", "winHouseText", "loseHouseText",
            "betMaximum", "propertyStage", "opponentLoss", "cardStage", "deckPath", "usedDeckPath",
            "playerCardsPath", "houseCardsPath", "texturesPath", "textureProperty", "stressGlobal",
            "interactionGlobal", "useGlobal", "resetState",
        };
        // Indices correspond to the fixed VenttiTableState outcome slots (None has no binding).
        public static readonly string[] OutcomeBindings = {
            "winText", "loseText", "winCarText", "loseCarText", "winHouseText", "loseHouseText",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public VenttiRules? Rules;
        public VenttiReactionData? Reactions;
        public string[] IdleStates = new string[0];
        public readonly List<VenttiOutcomeData> Outcomes = new List<VenttiOutcomeData>();
        public int MaterialIndex, CardSlots;
        public float PickDistance, WinStress, LoseStress;
        public string this[string key] => Bindings[key];
    }

    internal sealed class VenttiOutcomeData
    {
        public string State = "";
        public string[] ActionTypes = new string[0];
        public int[] SuppressActions = new int[0];
    }

    internal sealed class VenttiReactionData
    {
        public string RootPath = "";
        public string[] Poses = new string[0], Sounds = new string[0];
        public uint LayoutId;
        public readonly List<VenttiSoundSourceData> Sources = new List<VenttiSoundSourceData>();
    }
    internal sealed class VenttiSoundSourceData
    {
        public string Path = "", Fsm = "", State = "", Origin = "", Group = "", Variable = "", Variation = "";
        public int ActionIndex;
        public float Delay;
    }

    internal sealed class SlotMachineData
    {
        public readonly List<string> Paths = new List<string>();
        public string FsmName = "", IdleState = "", SpinState = "", FinishState = "", SoundPath = "";
        public string CreditVariable = "", WinningsVariable = "", LastWinVariable = "", BetVariable = "", BetButtonVariable = "";
        public string CanHoldVariable = "", RollVariable = "", RandomAction = "", HoldSwitchVariable = "";
        public string StatsPath = "", StatsFsm = "", StatsMoneyIn = "", StatsMoneyOut = "";
        public string AchievementObjectGlobal = "", AchievementFsm = "", AchievementEvent = "";
        public int AchievementThreshold;
        public string[] ButtonPaths = new string[0], ButtonStates = new string[0], RollStates = new string[0];
        public string[] ReelVariables = new string[0], ReelObjectVariables = new string[0], HoldVariables = new string[0];
        public string[] HoldStates = new string[0], TextPaths = new string[0];
        public int[][] Reels = new int[0][];
        public int[] Payouts = new int[0];
    }

    internal sealed class PokerData
    {
        public static readonly string[] RequiredBindings = {
            "path", "logicPath", "startupFsm", "buttonPath", "buttonFsm", "inputState", "inputAction", "meshPath",
            "game", "dealerFsm", "credit", "creditFsm", "creditVariable", "winnings", "winningsFsm", "winningsVariable",
            "bet", "betFsm", "betVariable", "camera", "menu", "menuFsm", "menuResetState", "menuIdleState", "menuBeginState", "balanceState",
            "screen", "doubleCard", "doubleBack", "doublePanel", "pendingMoney",
            "winMarker", "rankSuffix", "suitSuffix", "soundPath", "cardSound", "coinSound", "winSound", "loseSound",
            "achievementGlobal", "achievementFsm", "cashoutAchievement", "royalAchievement",
        };
        public static readonly string[] RequiredAssets = {
            "screenGame", "screenIdle", "doubleOffer", "doubleGuess", "buttonOn", "buttonOff", "creditText", "winsText", "pendingText",
        };
        public readonly Dictionary<string, string> Bindings = new Dictionary<string, string>();
        public readonly Dictionary<string, PokerAssetData> Assets = new Dictionary<string, PokerAssetData>();
        public string[] Buttons = new string[0], Cards = new string[0], Backs = new string[0], ButtonMeshes = new string[0], IdleStates = new string[0];
        public int[] Payouts = new int[0];
        public int AchievementThreshold;
        public float HeldZ, RestZ, MarkerX, MarkerStartZ, MarkerStepZ;
        public string this[string key] => Bindings[key];
    }
    internal sealed class PokerAssetData
    {
        public string Key = "", Path = "", Fsm = "", State = "", Action = "", Field = "";
    }
}
