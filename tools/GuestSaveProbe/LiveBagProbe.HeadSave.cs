using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static readonly string[] HeadSaveSuffixes = { "AID", "WEA", "TGH", "BLT", "VLV", "POS" };

        private void PrepareHeadSaveFixture()
        {
            string target = Environment.GetEnvironmentVariable("WINTERMP_MISSING_HEAD_FIXTURE");
            if (string.IsNullOrEmpty(target) || _role != "host") return;
            if (!Persistence || Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_HEAD_FIT_TEST") != "1")
                throw new InvalidOperationException("Head save fixture requires the isolated persistence test.");
            target = Path.GetFullPath(target);
            string folder = Path.GetDirectoryName(target);
            if (Path.GetFileName(target) != "carparts.txt"
                || File.ReadAllText(Path.Combine(folder, "wintermp-missing-head-sandbox.txt")) != "WinterMP missing head audit 20260913\n"
                || Path.GetFullPath(Application.persistentDataPath) == folder)
                throw new InvalidOperationException("Head save fixture must target a separate marked guest copy.");
            var getTags = typeof(ES2).GetMethod("GetTags", new[] { typeof(string) })
                ?? throw new InvalidOperationException("Native ES2 tag enumeration missing.");
            var before = (string[])getTags.Invoke(null, new object[] { target });
            string backup = Path.Combine(folder, "carparts-before-head-fixture.bin");
            if (File.Exists(backup)) throw new InvalidOperationException("Head fixture was already prepared.");
            File.Copy(target, backup);
            var removed = new HashSet<string>();
            foreach (string suffix in HeadSaveSuffixes)
            {
                string key = "VIN1110" + suffix;
                if (ES2.Exists(target + "?tag=" + key)) { ES2.Delete(target + "?tag=" + key); removed.Add(key); }
            }
            var after = new HashSet<string>((string[])getTags.Invoke(null, new object[] { target }));
            foreach (string key in before)
                if (after.Contains(key) == removed.Contains(key)) throw new InvalidOperationException("Unexpected fixture tag change.");
            if (after.Count != before.Length - removed.Count || removed.Count != HeadSaveSuffixes.Length)
                throw new InvalidOperationException("Missing expected native head save records.");
            var rows = new List<string> { "target|" + target, "before|" + before.Length, "after|" + after.Count };
            foreach (string key in removed) rows.Add("removed|" + key);
            File.WriteAllLines(Path.Combine(_output, "head-save-fixture.txt"), rows.ToArray());
        }

        private static bool HeadSaveCommand(string[] args, List<string> rows)
        {
            if (args[1] != "headsave-state") return false;
            if (Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_HEAD_FIT_TEST") != "1")
                throw new InvalidOperationException("Head fixture not enabled.");
            string file = Path.Combine(Application.persistentDataPath, "carparts.txt");
            foreach (string suffix in HeadSaveSuffixes) rows.Add("head-save-tag|" + suffix + "|" + ES2.Exists(file + "?tag=VIN1110" + suffix));
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = (PlayMakerFSM)obj;
                if (fsm.FsmName != "Data" || fsm.FsmVariables.FindFsmString("ID")?.Value != "VIN1110") continue;
                rows.Add("head-native|" + fsm.GetInstanceID() + "|" + fsm.gameObject.activeInHierarchy + "|" + fsm.ActiveStateName
                    + "|" + fsm.FsmVariables.FindFsmInt("AssemblyID").Value + "|" + fsm.FsmVariables.FindFsmBool("Consumed").Value
                    + "|" + fsm.enabled + "|" + fsm.FsmVariables.FindFsmString("UTAssemblyID").Value
                    + "|" + fsm.FsmVariables.FindFsmString("UTPos").Value);
                foreach (var component in fsm.GetComponents<Component>())
                {
                    var type = component.GetType();
                    if (type.Name != "PlayMakerArrayListProxy") continue;
                    string name = (string)type.GetField("referenceName").GetValue(component);
                    if (name != "Bolts" && name != "Valves") continue;
                    var values = new List<string>();
                    foreach (var value in (IList)type.GetProperty("arrayList").GetValue(component, null)) values.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                    rows.Add("head-native-array|" + name + "|" + string.Join(",", values.ToArray()));
                }
            }
            return true;
        }
    }
}
