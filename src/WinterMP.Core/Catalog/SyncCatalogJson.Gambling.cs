using System;
using System.Collections.Generic;

namespace WinterMP.Core.Catalog
{
    internal static partial class SyncCatalogJson
    {
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
