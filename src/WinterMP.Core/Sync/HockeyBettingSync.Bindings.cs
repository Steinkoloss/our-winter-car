using System;
using System.Collections;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class HockeyBettingSync
    {
        private sealed class Proxy
        {
            public MonoBehaviour Component = null!;
            public PropertyInfo Property = null!;
            public IList List()
            {
                var list = Component != null ? Property.GetValue(Component, null) as IList : null;
                if (list == null || list.IsReadOnly || list.IsFixedSize) throw new InvalidOperationException("Hockey live ArrayList unavailable.");
                return list;
            }
            public IDictionary Table()
            {
                var table = Component != null ? Property.GetValue(Component, null) as IDictionary : null;
                if (table == null || table.IsReadOnly || table.IsFixedSize) throw new InvalidOperationException("Hockey live Hashtable unavailable.");
                return table;
            }
        }

        private void Locate()
        {
            if (Ready) return;
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.HockeyBetting ?? throw new InvalidOperationException("Missing hockey catalog.");
            PlayMakerFSM? betting = null, season = null;
            var displays = new GameObject[c.Displays.Length];
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (path == c["bettingPath"] && fsm.FsmName == c["bettingFsm"])
                {
                    if (betting != null) throw new InvalidOperationException("Ambiguous hockey betting FSM.");
                    betting = fsm;
                }
                if (path == c["seasonPath"] && fsm.FsmName == c["seasonFsm"])
                {
                    if (season != null) throw new InvalidOperationException("Ambiguous hockey season FSM.");
                    season = fsm;
                }
                for (int i = 0; i < displays.Length; i++)
                {
                    if (!path.StartsWith(c.Displays[i] + "/", StringComparison.Ordinal)) continue;
                    var ancestor = fsm.transform;
                    while (ancestor != null && ScenePath.Of(ancestor) != c.Displays[i]) ancestor = ancestor.parent;
                    if (ancestor != null) displays[i] = ancestor.gameObject;
                }
            }
            if (betting == null || season == null || !betting.Fsm.Started || !season.Fsm.Started) return;
            foreach (var display in displays) if (display == null) return;
            foreach (string state in c.BettingIdle) RequireState(betting, state);
            foreach (string state in c.SeasonIdle) RequireState(season, state);
            var ints = new FsmInt[c.Ints.Length]; var floats = new FsmFloat[c.Floats.Length];
            for (int i = 0; i < ints.Length; i++) ints[i] = Variable(betting.FsmVariables.IntVariables, c.Ints[i]);
            for (int i = 0; i < floats.Length; i++) floats[i] = Variable(betting.FsmVariables.FloatVariables, c.Floats[i]);
            var lists = new Proxy[c.Lists.Length]; var tables = new Proxy[c.Tables.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = FindProxy(i == 2 || i == 3 ? betting : season, c.Lists[i], false);
            for (int i = 0; i < tables.Length; i++) tables[i] = FindProxy(betting, c.Tables[i], true);
            _result = Variable(betting.FsmVariables.StringVariables, c["result"]);
            _gamesPlayed = Variable(season.FsmVariables.IntVariables, c["gamesPlayed"]);
            _kurpaWins = Variable(season.FsmVariables.BoolVariables, c["kurpaWins"]);
            _c = c; _betting = betting; _season = season; _ints = ints; _floats = floats;
            _lists = lists; _tables = tables; _displays = displays;
            WinterMPPlugin.Log.LogInfo("Hockey: bound six odds tables, results, pairings and standings.");
        }

        private static T Variable<T>(T[] variables, string name) where T : NamedVariable
        {
            T? found = null;
            foreach (var value in variables)
            {
                if (value.Name != name) continue;
                if (found != null) throw new InvalidOperationException("Duplicate hockey variable: " + name);
                found = value;
            }
            return found ?? throw new InvalidOperationException("Missing typed hockey variable: " + name);
        }

        private static Proxy FindProxy(PlayMakerFSM fsm, string reference, bool table)
        {
            Proxy? found = null;
            string className = table ? "PlayMakerHashTableProxy" : "PlayMakerArrayListProxy";
            string propertyName = table ? "hashTable" : "arrayList";
            foreach (var component in fsm.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != className) continue;
                var type = component.GetType();
                if (type.GetField("referenceName")?.GetValue(component) as string != reference) continue;
                if (found != null) throw new InvalidOperationException("Duplicate hockey proxy: " + reference);
                var property = type.GetProperty(propertyName) ?? throw new InvalidOperationException("Missing hockey proxy live data.");
                found = new Proxy { Component = component, Property = property };
            }
            return found ?? throw new InvalidOperationException("Missing hockey proxy: " + reference);
        }

        private bool Stable() => _c != null && _betting != null && _season != null
            && Array.IndexOf(_c.BettingIdle, _betting.ActiveStateName) >= 0
            && Array.IndexOf(_c.SeasonIdle, _season.ActiveStateName) >= 0;

        private static void RequireState(PlayMakerFSM fsm, string state)
        {
            if (!FsmHook.HasState(fsm, state)) throw new InvalidOperationException("Missing hockey stable state: " + state);
        }

        private IList[] LiveLists()
        {
            var result = new IList[_lists.Length];
            for (int i = 0; i < result.Length; i++) result[i] = _lists[i].List();
            return result;
        }
        private IDictionary[] LiveTables()
        {
            var result = new IDictionary[_tables.Length];
            for (int i = 0; i < result.Length; i++) result[i] = _tables[i].Table();
            return result;
        }
        private static void Set(IList target, IEnumerable values)
        {
            target.Clear(); foreach (object value in values) target.Add(value);
        }
        private static void SetBytes(IList target, byte[] values, bool symbols)
        {
            target.Clear();
            foreach (byte value in values) target.Add(symbols ? (object)((char)value).ToString() : (int)value);
        }

        private void CaptureOriginal()
        {
            var lists = LiveLists(); var tables = LiveTables();
            var originals = new object[lists.Length][]; var originalTables = new IDictionary[tables.Length];
            for (int i = 0; i < lists.Length; i++)
            {
                originals[i] = new object[lists[i].Count]; lists[i].CopyTo(originals[i], 0);
            }
            for (int i = 0; i < tables.Length; i++) originalTables[i] = new Hashtable(tables[i]);
            _originalInts = new[] { _ints[0].Value, _gamesPlayed!.Value };
            _originalWin = _kurpaWins!.Value; _originalTables = originalTables; _originalLists = originals;
        }

        private void RefreshDisplays()
        {
            // Text FSMs read once on page enable. Refresh only the currently visible
            // text roots; inactive pages will read the complete collections when opened.
            foreach (var display in _displays)
            {
                if (display == null || !display.activeInHierarchy) continue;
                display.SetActive(false); display.SetActive(true);
            }
        }
    }
}
