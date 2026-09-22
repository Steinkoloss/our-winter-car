using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Core.UI;

namespace WinterMP.GuestSaveProbe
{
    // Isolated native sleep driver; no save operations, no normal-install activation.
    internal sealed class LiveSleepProbe
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private string _output = string.Empty, _role = string.Empty;
        private float _readyAt = -1, _nextPoll;
        private int _sequence;
        internal void Start()
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-live-sleep-sandbox.txt")))
                throw new InvalidOperationException("Live sleep probe requires a marked isolated copy.");
            _output = Path.Combine(BepInEx.Paths.GameRootPath, "live-sleep"); Directory.CreateDirectory(_output);
            _role = Environment.GetEnvironmentVariable("WINTERMP_LOG_ROLE") == "guest" ? "guest" : "host";
        }
        internal void Tick()
        {
            if (Application.loadedLevelName != "GAME" || SessionManager.Instance == null) return;
            if (_readyAt < 0) _readyAt = Time.unscaledTime + 15;
            if (Time.unscaledTime < _readyAt || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + .1f;
            string command = Path.Combine(_output, _role + "-command.txt");
            if (!File.Exists(command)) return;
            var args = File.ReadAllText(command).TrimEnd('\r', '\n').Split('\t');
            int sequence = int.Parse(args[0], CultureInfo.InvariantCulture);
            if (sequence <= _sequence) return;
            _sequence = sequence;
            var rows = new List<string>();
            try { Execute(args); Snapshot(rows); rows.Insert(0, "OK|" + sequence); }
            catch (Exception error) { rows.Insert(0, "FAIL|" + sequence); rows.Add(error.ToString()); }
            File.WriteAllLines(Path.Combine(_output, _role + "-" + sequence + ".txt"), rows.ToArray());
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
        private static PlayMakerFSM Find(string path, string name)
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = (PlayMakerFSM)obj;
                if (fsm.FsmName == name && PathOf(fsm.transform) == path) return fsm;
            }
            throw new InvalidOperationException("Missing " + path + "::" + name);
        }
        private static string PathOf(Transform node)
        { string path = node.name; while (node.parent != null) { node = node.parent; path = node.name + "/" + path; } return path; }
        private static PlayMakerFSM Bed => Find("HOMENEW/Functions/FunctionsDisable/Sleep/SleepTrigger", "Activate");
        private static void Execute(string[] args)
        {
            var session = SessionManager.Instance!;
            if (args[1] == "snapshot") return;
            if (args[1] == "answer")
            { typeof(SleepConsentPrompt).GetMethod("Respond", Members).Invoke(SleepConsentPrompt.Instance, new object[] { args[2] == "yes" }); return; }
            if (args[1] == "leave") { session.Shutdown("Isolated sleep test disconnect"); return; }
            if (args[1] == "join") { session.StartJoinLocal("127.0.0.1", WinterMP.Net.Transport.UdpTransport.DefaultPort); return; }
            var bed = Bed;
            if (!bed.enabled || !bed.gameObject.activeInHierarchy || !bed.Fsm.Initialized)
                throw new InvalidOperationException("Native bed is not active and initialized.");
            if (args[1] == "cancel") { bed.SendEvent("FINISHED"); return; }
            if (args[1] == "timeout")
            { typeof(SleepConsentManager).GetField("_consentDeadlineAt", Members).SetValue(SleepConsentManager.Instance, 0f); return; }
            if (args[1] != "begin") throw new InvalidOperationException("Unknown sleep command.");
            // Start after the physical bed-use gesture. The remainder is the real
            // FSM: native wait, animations, clock progression, fatigue and wake-up.
            if (session.IsHost)
            {
                var player = FsmVariables.GlobalVariables.FindFsmGameObject("SavePlayer")?.Value;
                var pivot = bed.FsmVariables.FindFsmGameObject("AnimPivot")?.Value;
                if (player == null || pivot == null) throw new InvalidOperationException("Sleep anchors missing.");
                player.transform.position = pivot.transform.position;
                FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue").Value = 30f;
            }
            var hook = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmHook", true);
            hook.GetMethod("EnsureRemoteEntry").Invoke(null, new object[] { bed, "Confirm" });
            hook.GetMethod("FireRemoteEntry").Invoke(null, new object[] { bed, "Confirm" });
        }
        private static void Snapshot(List<string> rows)
        {
            var session = SessionManager.Instance!;
            var manager = SleepConsentManager.Instance!;
            rows.Add("session|" + session.State + "|" + session.PlayerCount);
            rows.Add("round|" + Get(manager, "_activeRequestId") + "|" + Get(manager, "_waitingForGuests")
                + "|" + Get(manager, "_consentGranted") + "|" + Get(manager, "_awaitingPostSleepSync"));
            rows.Add("prompt|" + SleepConsentPrompt.Instance!.IsBlockingInput);
            rows.Add("bed|" + Bed.ActiveStateName + "|" + Bed.gameObject.activeInHierarchy);
            var globals = FsmVariables.GlobalVariables;
            foreach (var name in new[] { "PlayerFatigue", "PlayerHunger", "PlayerThirst", "GlobalTimeScale" })
                rows.Add("float|" + name + "|" + globals.FindFsmFloat(name)?.Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (var name in new[] { "PlayerStop", "PlayerSleeps" })
                rows.Add("bool|" + name + "|" + globals.FindFsmBool(name)?.Value);
            var clock = Find("MAP/Sun/PivotSun/SUN", "Color");
            rows.Add("clock|" + clock.FsmVariables.FindFsmInt("Time")?.Value + "|"
                + clock.FsmVariables.FindFsmFloat("Minutes")?.Value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
