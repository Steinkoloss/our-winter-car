using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool PermadeathProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_PERMADEATH_TEST") == "1";
        private const string PermaSave = "savefile.txt?tag=PlayerPermaDeath";
        private static FsmBool PermaFlag => FsmVariables.GlobalVariables.FindFsmBool("PlayerPermaDeath");
        private static PlayMakerFSM? _permaDeath;
        private static readonly Dictionary<string, int> PermaEntries = new Dictionary<string, int>();

        private static bool PermadeathCommand(string[] args, List<string> rows)
        {
            if (!PermadeathProbe || !args[1].StartsWith("perma-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            switch (args[1])
            {
                case "perma-local": PermaFlag.Value = bool.Parse(args[2]); return true;
                case "perma-load": RunPermaAction(false); return true;
                case "perma-save":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Only the disposable host may change its save fixture.");
                    PermaFlag.Value = bool.Parse(args[2]); RunPermaAction(true); return true;
                case "perma-guarded-save":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest save guard check only.");
                    RunPermaAction(true); return true;
                case "perma-menu": Application.LoadLevel("MainMenu"); return true;
                case "perma-die":
                    ObservePermaDeath();
                    if (_permaDeath == null || _permaDeath.gameObject.activeInHierarchy) throw new InvalidOperationException("Inactive native death graph required.");
                    _permaDeath.FsmVariables.FindFsmBool("Fatigue").Value = true;
                    _permaDeath.gameObject.SetActive(true); return true;
                case "perma-death-next":
                    if (_permaDeath == null || (_permaDeath.ActiveStateName != "Newspaper" && _permaDeath.ActiveStateName != "Orbituary"
                        && _permaDeath.ActiveStateName != "State 2")) throw new InvalidOperationException("Native death screen required.");
                    _permaDeath.SendEvent("FINISHED"); return true;
                case "perma-report":
                    SessionManager.Instance!.SendPlayerProfileMessage(new PlayerDeathReport { PlayerId = SessionManager.Instance.LocalPlayerId,
                        Cause = DeathCause.Fatigue, Sequence = 1 }); return true;
                case "perma-respawn":
                    SessionManager.Instance!.SendPlayerProfileMessage(new PlayerRespawn { PlayerId = SessionManager.Instance.LocalPlayerId,
                        Position = new NetVector3(1, 2, 3), Rotation = NetQuaternion.Identity, Sequence = 2 }); return true;
                case "perma-continue":
                    if (Application.loadedLevelName != "MainMenu") throw new InvalidOperationException("Menu required.");
                    using (var log = new BepInEx.Logging.ManualLogSource("Permadeath probe"))
                        new WinterMP.FastBoot.MenuContinue().TryAdvance(log, false);
                    return true;
                default: throw new InvalidOperationException("Unknown permadeath command.");
            }
        }

        private static void RunPermaAction(bool save)
        {
            var root = new GameObject("Permadeath action probe");
            try
            {
                var fsm = root.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
                if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                var state = new FsmState(fsm.Fsm) { Name = "Probe" };
                var type = Assembly.Load("Assembly-CSharp").GetType("HutongGames.PlayMaker.Actions." + (save ? "SaveBool" : "LoadBool"), true);
                var action = (FsmStateAction)Activator.CreateInstance(type); action.Reset();
                type.GetField(save ? "saveValue" : "loadValue").SetValue(action, PermaFlag);
                type.GetField("uniqueTag").SetValue(action, new FsmString { Value = "PlayerPermaDeath" });
                type.GetField("saveFile").SetValue(action, new FsmString { Value = "savefile.txt" });
                action.Init(state); action.OnEnter();
            }
            finally { UnityEngine.Object.Destroy(root); }
        }

        private static void PermadeathSnapshot(List<string> rows)
        {
            if (!PermadeathProbe) return;
            RequirePersistenceSandbox();
            ObservePermaDeath();
            rows.Add("perma|" + SessionManager.Instance!.PermanentDeathEnabled + "|" + PermaFlag.Value
                + "|" + ES2.Exists(PermaSave) + "|" + (ES2.Exists(PermaSave) ? ES2.Load<bool>(PermaSave).ToString() : "missing"));
            var settings = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.PermadeathSettings", true);
            var args = new object[] { false };
            var read = settings.GetMethod("TryRead", Members).Invoke(null, args);
            rows.Add("perma-read|" + read + "|" + args[0]);
            var manager = DeathSyncManager.Instance;
            if (manager != null) rows.Add("perma-dead|" + manager.GetType().GetProperty("IsLocalDead", Members).GetValue(manager, null));
            foreach (var peer in SessionManager.Instance.Players) rows.Add("perma-peer|" + peer.PlayerId + "|" + peer.IsDead);
            foreach (var entry in PermaEntries) rows.Add("perma-entry|" + entry.Key + "|" + entry.Value);
            if (_permaDeath != null) rows.Add("perma-death|" + _permaDeath.ActiveStateName + "|" + _permaDeath.gameObject.activeInHierarchy);
            foreach (string file in new[] { "savefile.txt", "carparts.txt", "items2.txt", "meshsave.txt", "trophies.txt", "hockeyleague.txt", "speedcam.txt", "graveyard.txt" })
                rows.Add("perma-file|" + file + "|" + ES2.Exists(file));
            rows.Add("perma-chat|" + string.Join(" ~ ", new List<string>(SessionManager.Instance.ChatLog).ToArray()));
        }

        private static void ObservePermaDeath()
        {
            if (Application.loadedLevelName != "GAME" || _permaDeath != null) return;
            var fsm = Find("Systems/Death", "Activate Dead Body");
            if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            var hook = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmHook", true)
                .GetMethod("OnStateEnter", Members, null, new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null);
            foreach (string state in new[] { "Permadeath 2", "Delete saves 2", "State 3", "Take photo", "Delete saves", "Newspaper", "State 2", "Save all", "Orbituary" })
            {
                string captured = state;
                hook.Invoke(null, new object[] { fsm, state, (Action)(() => {
                    int n; PermaEntries.TryGetValue(captured, out n); PermaEntries[captured] = n + 1;
                }) });
            }
            _permaDeath = fsm;
        }
    }
}
