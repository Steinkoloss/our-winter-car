using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Core.UI;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunBodyWarmthChecks(Action<string, Action> check, SessionManager session, CaptureTransport capture, Action reset)
        {
            var globals = FsmVariables.GlobalVariables; var oldGlobals = globals.FloatVariables;
            var warmth = new FsmFloat { Name = "PlayerTemp", UseVariable = true, Value = 54.5f };
            var testGlobals = new List<FsmFloat>(oldGlobals);
            foreach (string name in new[] { "PlayerTemp", "PlayerHunger", "PlayerFatigue", "PlayerThirst", "PlayerUrine", "PlayerStress", "PlayerDirtiness", "PlayerAlco" })
            {
                testGlobals.RemoveAll(v => v.Name == name);
                testGlobals.Add(name == "PlayerTemp" ? warmth : new FsmFloat { Name = name, UseVariable = true, Value = 7 });
            }
            var store = Core.GetType("WinterMP.Core.Session.GuestProfileStore", true);
            var profiles = (Dictionary<ulong, GuestProfile>)store.GetField("Profiles", Static).GetValue(null);
            var oldProfiles = new Dictionary<ulong, GuestProfile>(profiles); bool loaded = (bool)store.GetField("_loaded", Static).GetValue(null);
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
            var policy = (GuestSavePolicy)guard.GetField("Policy", Static).GetValue(null); bool protectedWorld = policy.ProtectWorld;
            var peers = (IDictionary)Get(session, "_playersByPeer"); var peer = new PeerId(1001); var one = (RemotePlayer)peers[peer];
            ulong steam = one.SteamId; bool returning = one.ReturningGuest, hadNeeds = one.HasNeedsReport; ushort oldSequence = one.LastNeedsSequence;
            const ulong testId = 76561190000000189;
            var go = new GameObject("native body warmth probe"); go.SetActive(false);
            var row = NativeBagPartChecks.Find(ReadOccupancyRows("body-warmth-probe.json"), "PLAYER/BodyTemp", "Calculations");
            var fsm = NativeBagPartChecks.MakeFsm(go, row); fsm.Fsm.Init(fsm);
            foreach (var state in fsm.FsmStates) { state.Actions = new FsmStateAction[0]; state.Transitions = new FsmTransition[0]; }
            var calculate = NativeBagPartChecks.State(fsm, "Calculate");
            foreach (Dictionary<string, object> state in (IEnumerable)row["states"])
                if ((string)state["name"] == "Calculate")
                {
                    var actions = new List<FsmStateAction>();
                    foreach (Dictionary<string, object> a in (IEnumerable)state["actions"]) actions.Add(ReadCondensationAction(a, fsm));
                    calculate.Actions = actions.ToArray(); foreach (var action in calculate.Actions) action.Init(calculate);
                }
            Set(calculate.Actions[3], "floatVariable", warmth);
            var ambient = fsm.FsmVariables.FindFsmFloat("Temperature");
            var syncType = Core.GetType("WinterMP.Core.Sync.PlayerNeedsSync", true);
            var sync = Activator.CreateInstance(syncType, true);
            var oldPlayerSync = PlayerSyncManager.Instance; var oldPrompt = GuestSpawnPrompt.Instance;
            var playerSync = go.AddComponent<PlayerSyncManager>(); playerSync.enabled = false;
            var prompt = go.AddComponent<GuestSpawnPrompt>(); prompt.enabled = false;
            Func<PlayerNeedsReport> report = () => Call(sync, "BuildReport", (byte)3) as PlayerNeedsReport
                ?? throw new InvalidOperationException("Missing needs report.");
            Action<bool> availability = available =>
            {
                var list = new List<FsmFloat>(testGlobals); if (!available) list.Remove(warmth);
                globals.FloatVariables = list.ToArray(); Call(sync, "Locate", true);
            };
            Action<PlayerNeedsReport, PeerId> receive = (message, sender) => Call(session, "HandleMessage", sender, message, Channel.ReliableOrdered);
            Func<GuestSpawn> offer = () => (GuestSpawn)typeof(SessionManager).GetMethod("BuildGuestSpawn", Static).Invoke(null, new object[] { one });
            Action clean = () =>
            {
                reset(); profiles.Clear(); store.GetField("_loaded", Static).SetValue(null, true);
                SetProperty(policy, "ProtectWorld", true); one.SteamId = testId; one.ReturningGuest = true;
                one.HasNeedsReport = false; one.LastNeedsSequence = 0;
                Call(sync, "Reset"); warmth.Value = 54.5f; ambient.Value = -15; availability(true); capture.Packets.Clear();
            };
            try
            {
                globals.FloatVariables = testGlobals.ToArray(); go.SetActive(true); NativeBagPartChecks.Start(fsm);
                SetStaticProperty(typeof(PlayerSyncManager), "Instance", playerSync); SetStaticProperty(typeof(GuestSpawnPrompt), "Instance", prompt);
                check("body warmth: report reads native PlayerTemp and leaves ambient scratch alone", () =>
                {
                    clean(); var message = report();
                    Require(message.HasBodyTemp && message.BodyTemp == 54.5f && ambient.Value == -15, "Report used environmental temperature.");
                    var snapshot = (GuestProfile.NeedsSnapshot)(Call(sync, "ReadSnapshot")
                        ?? throw new InvalidOperationException("Missing needs snapshot."));
                    Require(snapshot.Valid && snapshot.HasBodyTemp && snapshot.BodyTemp == 54.5f, "Snapshot lost native warmth.");
                });
                check("body warmth: imported native Calculate updates the same warmth global that is reported", () =>
                {
                    clean(); ambient.Value = 30; fsm.FsmVariables.FindFsmFloat("AirSpeed").Value = 20; warmth.Value = 60;
                    NativeBagPartChecks.Fire(fsm, "Calculate");
                    Require(warmth.Value == 61 && ambient.Value == 30 && report().BodyTemp == 61, "Native body calculation and report disagree.");
                });
                check("body warmth: guest reports use the existing reliable channel while host does not report", () =>
                {
                    clean(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected); SetProperty(session, "LocalPlayerId", (byte)3);
                    Call(sync, "UpdateGuest", session);
                    Require(capture.Packets.Count == 1 && capture.Packets[0].Channel == Channel.ReliableOrdered
                        && ((PlayerNeedsReport)capture.Packets[0].Message).BodyTemp == 54.5f, "Warmth sender did not preserve report delivery.");
                    capture.Packets.Clear(); SetProperty(session, "IsHost", true); Set(sync, "_nextReportAt", 0f); Call(sync, "UpdateGuest", session);
                    Require(capture.Packets.Count == 0, "Host reported itself as a guest.");
                });
                check("body warmth: missing and nonfinite native globals report unavailable zero", () =>
                {
                    clean(); availability(false); Require(!report().HasBodyTemp && report().BodyTemp == 0, "Missing warmth was fabricated.");
                    availability(true);
                    foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                    { warmth.Value = invalid; var r = report(); Require(!r.HasBodyTemp && r.BodyTemp == 0, "Nonfinite warmth escaped."); }
                });
                check("body warmth: known zero and below-zero restore without changing the air input", () =>
                {
                    clean(); foreach (float value in new[] { 0f, -5f, 72.25f })
                    {
                        Call(sync, "ApplySnapshot", new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = value });
                        Require(warmth.Value == value && ambient.Value == -15, "Warmth restore used a zero sentinel or ambient write.");
                    }
                });
                check("body warmth: delayed binding keeps host warmth in reports and restores it once", () =>
                {
                    clean(); availability(false);
                    Call(sync, "ApplySnapshot", new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = 0 });
                    Require(report().HasBodyTemp && report().BodyTemp == 0, "Pending host warmth was forgotten.");
                    availability(true); Require(warmth.Value == 0, "Late global failed to receive known zero.");
                    warmth.Value = 8; Call(sync, "Locate", true); Require(warmth.Value == 8, "Pending restore repeated after native progression.");
                });
                check("body warmth: newer unknown snapshot and reset cancel obsolete deferred warmth", () =>
                {
                    clean(); availability(false);
                    Call(sync, "ApplySnapshot", new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = 75 });
                    Call(sync, "ApplySnapshot", new GuestProfile.NeedsSnapshot { Valid = true });
                    availability(true); Require(warmth.Value == 54.5f, "Unknown snapshot revived previous warmth.");
                    availability(false); Call(sync, "ApplySnapshot", new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = 75 });
                    Call(sync, "Reset"); availability(true); Require(warmth.Value == 54.5f, "Reset retained deferred warmth.");
                });
                check("body warmth: malformed local snapshot cannot poison native warmth", () =>
                {
                    clean(); Call(sync, "ApplySnapshot", new GuestProfile.NeedsSnapshot { Valid = true, HasBodyTemp = true, BodyTemp = float.NaN });
                    Require(warmth.Value == 54.5f && ambient.Value == -15, "Malformed snapshot poisoned body warmth.");
                });
                check("body warmth: authenticated first-zero report persists and builds a known reconnect offer", () =>
                {
                    clean(); receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 0, HasBodyTemp = true, BodyTemp = 0 }, peer);
                    Require(profiles.ContainsKey(testId) && profiles[testId].Needs.HasBodyTemp && profiles[testId].Needs.BodyTemp == 0, "Host lost known zero.");
                    var spawn = offer(); Require(spawn.HasSavedNeeds && spawn.HasSavedBodyTemp && spawn.BodyTemp == 0, "Reconnect offer omitted known warmth.");
                });
                check("body warmth: forged stale duplicate and malformed reports cannot change host warmth", () =>
                {
                    clean(); receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 10, HasBodyTemp = true, BodyTemp = 42 }, peer);
                    foreach (ushort seq in new ushort[] { 9, 10 }) receive(new PlayerNeedsReport { PlayerId = 1, Sequence = seq, HasBodyTemp = true, BodyTemp = 80 }, peer);
                    receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 11, HasBodyTemp = true, BodyTemp = 80 }, new PeerId(1002));
                    receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 11, HasBodyTemp = true, BodyTemp = float.NaN }, peer);
                    receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 11, HasBodyTemp = true, BodyTemp = 80,
                        HasAlco = true, PlayerAlco = float.NaN }, peer);
                    Require(profiles[testId].Needs.BodyTemp == 42 && one.LastNeedsSequence == 10, "Rejected report refreshed profile or sequence.");
                });
                check("body warmth: wraparound accepts explicit unavailable and clears only body availability", () =>
                {
                    clean(); receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 65535, HasBodyTemp = true, BodyTemp = 42 }, peer);
                    receive(new PlayerNeedsReport { PlayerId = 1, Sequence = 0, HasAlco = true, PlayerAlco = 1.5f, Hunger = 11 }, peer);
                    var n = profiles[testId].Needs; Require(!n.HasBodyTemp && n.BodyTemp == 0 && n.HasAlco && n.Hunger == 11, "Unavailable report lost unrelated needs.");
                    Require(!offer().HasSavedBodyTemp, "Unknown body warmth appeared in reconnect offer.");
                });
                check("body warmth: legacy air sample stays unknown through store reconnect and UI restore", () =>
                {
                    clean(); ulong id; GuestProfile p;
                    Require(GuestProfile.TryParse(testId + ",1,2,3,0,0,0,1,11,12,13,14,55,15,16,17,18", out id, out p), "Legacy fixture parse failed.");
                    profiles[id] = p; var spawn = offer();
                    Require(!spawn.HasSavedBodyTemp && spawn.BodyTemp == 0 && spawn.HasSavedDirtiness && spawn.HasSavedAlco, "Legacy migration discarded unrelated fields.");
                    Call(prompt, "ApplySavedNeeds", spawn); Require(warmth.Value == 54.5f && ambient.Value == -15, "Legacy air sample became body warmth.");
                });
                foreach (string choice in new[] { "ChooseHost", "ChooseLast" })
                    check("guest resume: " + choice + " restores saved needs before reports resume", () =>
                    {
                        clean(); SetProperty(session, "IsHost", false);
                        Call(playerSync, "ResetGuestSpawn");
                        var gate = (GuestResumePolicy)Get(playerSync, "_guestResume"); gate.ReceiveOffer(true);
                        var spawn = new GuestSpawn { Flags = GuestSpawn.FlagHasLastPosition | GuestSpawn.FlagHasSavedNeeds
                            | GuestSpawn.FlagHasSavedBodyTemp, Hunger = 25, Fatigue = 35, Thirst = 45, Urine = 55, BodyTemp = 67.5f };
                        prompt.ShowOffer(spawn);
                        Require(prompt.IsBlockingInput && !gate.CanPublish(true), "Unanswered offer allowed reports.");
                        Call(prompt, choice); gate.CompleteRelocation();
                        Require(!prompt.IsBlockingInput && gate.CanPublish(true), "Completed choice did not release reports.");
                        Require(globals.FindFsmFloat("PlayerHunger").Value == 25 && globals.FindFsmFloat("PlayerUrine").Value == 55
                            && warmth.Value == 67.5f && ambient.Value == -15, "Spawn choice lost the host profile.");
                    });
                check("guest resume: native global save freezes reports while ordinary events and host saves remain active", () =>
                {
                    clean(); var gate = (GuestResumePolicy)Get(playerSync, "_guestResume");
                    gate.Reset(); gate.ReceiveOffer(false); gate.CompleteRelocation();
                    Type? actionType = null;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        if ((actionType = assembly.GetType("HutongGames.PlayMaker.Actions.SendEvent")) != null) break;
                    var action = Activator.CreateInstance(actionType ?? throw new InvalidOperationException("Native event action missing."));
                    var target = new FsmEventTarget { target = FsmEventTarget.EventTarget.BroadcastAll };
                    actionType.GetField("eventTarget").SetValue(action, target);
                    actionType.GetField("sendEvent").SetValue(action, FsmEvent.GetFsmEvent("SAVEGAME"));
                    var before = guard.GetMethod("BeforeSendEvent", Static);
                    before.Invoke(null, new[] { action }); Require(gate.CanPublish(true), "Host save stopped profile sync.");
                    SetProperty(session, "IsHost", false);
                    actionType.GetField("sendEvent").SetValue(action, FsmEvent.GetFsmEvent("ORDINARY"));
                    before.Invoke(null, new[] { action }); Require(gate.CanPublish(true), "Ordinary event stopped sync.");
                    actionType.GetField("sendEvent").SetValue(action, FsmEvent.GetFsmEvent("SAVEGAME"));
                    target.target = FsmEventTarget.EventTarget.GameObjectFSM;
                    before.Invoke(null, new[] { action }); Require(gate.CanPublish(true), "Targeted save stopped sync.");
                    target.target = FsmEventTarget.EventTarget.BroadcastAll;
                    before.Invoke(null, new[] { action });
                    Require(!gate.CanPublish(true) && !gate.ReceiveOffer(true), "Save teardown or late offer overwrote the profile.");
                });
                check("body warmth: known reconnect offer reaches the local needs restore without an ambient write", () =>
                {
                    clean(); receive(new PlayerNeedsReport { PlayerId = 1, HasBodyTemp = true, BodyTemp = 67.5f }, peer);
                    var spawn = (GuestSpawn)PacketCodec.Decode(PacketCodec.Encode(offer()));
                    Call(prompt, "ApplySavedNeeds", spawn); Require(warmth.Value == 67.5f && ambient.Value == -15, "UI mapping dropped warmth or wrote ambient.");
                });
            }
            finally
            {
                globals.FloatVariables = oldGlobals; profiles.Clear(); foreach (var pair in oldProfiles) profiles.Add(pair.Key, pair.Value);
                store.GetField("_loaded", Static).SetValue(null, loaded); SetProperty(policy, "ProtectWorld", protectedWorld);
                one.SteamId = steam; one.ReturningGuest = returning; one.HasNeedsReport = hadNeeds; one.LastNeedsSequence = oldSequence;
                UnityEngine.Object.DestroyImmediate(go);
                SetStaticProperty(typeof(PlayerSyncManager), "Instance", oldPlayerSync); SetStaticProperty(typeof(GuestSpawnPrompt), "Instance", oldPrompt);
                reset();
            }
        }
    }
}
