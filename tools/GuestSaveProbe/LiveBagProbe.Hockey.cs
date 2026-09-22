using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool HockeyProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_HOCKEY_TEST") == "1";
        private static object Hockey => Get(WorldSyncManager.Instance!, "_hockey");
        private static Array? _hockeyLists, _hockeyTables;
        private static PlayMakerFSM? _hockeyBetting, _hockeySeason;

        private static bool HockeyCommand(string[] args)
        {
            if (!HockeyProbe || !args[1].StartsWith("hockey-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            switch (args[1])
            {
                case "hockey-menu": Application.LoadLevel("MainMenu"); return true;
                case "hockey-continue":
                    if (Application.loadedLevelName != "MainMenu") throw new InvalidOperationException("Menu required.");
                    using (var log = new BepInEx.Logging.ManualLogSource("Hockey reload probe"))
                        new WinterMP.FastBoot.MenuContinue().TryAdvance(log, false);
                    return true;
                case "hockey-odds":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var tables = (IDictionary[])Call(Hockey, "LiveTables");
                    float odds = float.Parse(args[2], CultureInfo.InvariantCulture);
                    if (odds < 1 || odds > 100 || float.IsNaN(odds)) throw new InvalidOperationException("Invalid test odds.");
                    tables[0]["1"] = odds; return true;
                default: throw new InvalidOperationException("Unknown hockey test command.");
            }
        }

        private static void HockeySnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            var sync = Hockey;
            var lists = (Array)Get(sync, "_lists"); var tables = (Array)Get(sync, "_tables");
            if (lists.Length != 0)
            {
                _hockeyLists = lists; _hockeyTables = tables;
                _hockeyBetting = Get(sync, "_betting") as PlayMakerFSM;
                _hockeySeason = Get(sync, "_season") as PlayMakerFSM;
            }
            rows.Add("hockey-sync|" + lists.Length + "|" + tables.Length + "|" + Get(sync, "_disabled")
                + "|" + (Get(sync, "_applied") != null) + "|" + (Get(sync, "_originalLists") != null));
            rows.Add("hockey-native|" + NativeHockey(_hockeyBetting) + "|" + NativeHockey(_hockeySeason));
            var originalLists = Get(sync, "_originalLists") as object[][];
            if (originalLists != null)
                rows.Add("hockey-original|" + HockeyHash(originalLists, (IDictionary[])Get(sync, "_originalTables"),
                    (int[])Get(sync, "_originalInts"), (bool)Get(sync, "_originalWin")));
            if (_hockeyLists == null || _hockeyTables == null || _hockeyBetting == null || _hockeySeason == null) return;
            var liveLists = new IList[_hockeyLists.Length]; var liveTables = new IDictionary[_hockeyTables.Length];
            for (int i = 0; i < liveLists.Length; i++) liveLists[i] = (IList)Call(_hockeyLists.GetValue(i), "List");
            for (int i = 0; i < liveTables.Length; i++) liveTables[i] = (IDictionary)Call(_hockeyTables.GetValue(i), "Table");
            int[] ints = { _hockeyBetting.FsmVariables.FindFsmInt("LatestRound").Value,
                _hockeySeason.FsmVariables.FindFsmInt("GamesPlayed").Value };
            rows.Add("hockey-live|" + HockeyHash(liveLists, liveTables, ints,
                _hockeySeason.FsmVariables.FindFsmBool("KurPaWins").Value));
            rows.Add("hockey-odds|" + ((float)liveTables[0]["1"]).ToString("R", CultureInfo.InvariantCulture));
            rows.Add("hockey-sizes|" + string.Join(",", Array.ConvertAll(liveLists, list => list.Count.ToString())));
        }

        private static string NativeHockey(PlayMakerFSM? fsm) => fsm == null ? "destroyed"
            : fsm.GetInstanceID() + ":" + fsm.ActiveStateName + ":" + fsm.enabled + ":" + fsm.Fsm.RestartOnEnable;

        private static string HockeyHash(IList[] lists, IDictionary[] tables, int[] ints, bool win)
        {
            var text = new StringBuilder();
            foreach (var list in lists)
            {
                text.Append('['); foreach (var value in list) HockeyValue(text, value); text.Append(']');
            }
            foreach (var table in tables)
            {
                var keys = new List<string>(); foreach (var key in table.Keys) keys.Add((string)key);
                keys.Sort(StringComparer.Ordinal);
                text.Append('{'); foreach (var key in keys) { HockeyValue(text, key); HockeyValue(text, table[key]); } text.Append('}');
            }
            foreach (var value in ints) HockeyValue(text, value);
            HockeyValue(text, win);
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static void HockeyValue(StringBuilder text, object value)
        {
            string formatted = value is float f ? f.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
            text.Append(value.GetType().Name).Append(':').Append(formatted.Length).Append(':').Append(formatted).Append(';');
        }
    }
}
