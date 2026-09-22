using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private static void RunGearboxConditionChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport capture, Action reset)
        {
            var itemsObject = Get(vehicles, "_items"); var items = (IDictionary)Get(itemsObject, "_items");
            var world = World.GetProperty("Instance", Static).GetValue(null, null);
            var player = (Transform)World.GetProperty("LocalPlayer", Members).GetValue(world, null);
            var parent = player.parent; var position = player.position;
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            using (var f = new GearboxConditionFixture())
            try
            {
                var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                items[VehicleId] = item; Set(item, "Id", VehicleId); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true);
                Action clean = () =>
                {
                    reset(); SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Connected); SetProperty(session, "LocalPlayerId", (byte)3);
                    Set(item, "Body", f.Body); Set(item, "RemoteOwner", (byte)1); Set(item, "LocallyOwned", false);
                    Set(item, "LastRemoteSequence", (ushort)0); Set(item, "LastRemoteSequenceOwner", (byte)255);
                    Set(item, "RemoteIsDriver", false); Set(item, "RemoteVehicleStream", false); Set(item, "LastRemoteAt", -999f);
                    player.SetParent(parent, false); player.position = position;
                    Call(vehicles, "ClearConditionStreams"); f.Seed(); capture.Packets.Clear();
                };
                Func<byte, ushort, VehicleCondition> state = (damage, sequence) => new VehicleCondition {
                    VehicleId = VehicleId, OwnerPlayerId = 1, Sequence = sequence,
                    Availability = VehicleCondition.AvailableDrivetrain, DrivetrainDamage = damage };
                Action<VehicleCondition> packet = message => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(message), Channel.ReliableOrdered);
                Action saved = () => Require(f.SavedDamage.Value == 3 && f.Wear.Value == 90, "Shared condition changed saved gearbox data.");
                Action<byte> damageIs = value =>
                {
                    f.Read(); Require(f.Damage.Value == value && f.Consumer.ActiveStateName == (value >= 1 && value <= 3 ? "Type " + value : "Delay"),
                        "Native gearbox selected a branch from local saved damage instead of the eligible input."); saved();
                };
                Func<bool> prepare = () => (bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true });
                Action<byte> claim = value =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)255); var initial = state(value, 65535); initial.OwnerPlayerId = 0; packet(initial);
                    player.position = f.Body.transform.position;
                    Require((bool)Call(itemsObject, "TryClaimForInteraction", session, item)!, "Actual gearbox-input physics claim failed.");
                };

                foreach (byte value in new byte[] { 0, 1, 255 })
                {
                    byte expected = value;
                    check("gearbox condition claim: native driver branch retains available damage " + expected, () =>
                    { claim(expected); damageIs(expected); damageIs(expected); });
                }
                check("gearbox condition claim: outgoing state before a native check uses copied shared damage", () =>
                {
                    claim(1); f.Damage.Value = 3; capture.Packets.Clear(); Call(vehicles, "UpdateVehicleCondition", session);
                    int count = 0; foreach (var sent in capture.Packets) if (sent.Message is VehicleCondition input)
                    { count++; Require(input.HasDrivetrain && input.DrivetrainDamage == 1 && input.OwnerPlayerId == 3, "First report used saved gearbox damage."); }
                    Require(count == 1, "First claim condition was not reported."); saved();
                });
                check("gearbox condition claim: native reverse wear remains protected while driving", () =>
                {
                    claim(1); damageIs(1); NativeBagPartChecks.Fire(f.Consumer, "Reverse");
                    Require(!f.Writer.Enabled && !NativeBagPartChecks.State(f.Consumer, "Reverse").ActiveActions.Contains(f.Writer), "Driver scheduled saved gearbox wear."); saved();
                });
                check("gearbox condition claim: teardown retires the driver input and restores native lookup", () =>
                { claim(1); damageIs(1); Call(vehicles, "ClearConditionStreams"); damageIs(3); });

                foreach (byte value in new byte[] { 0, 1, 2, 3, 255 })
                {
                    byte expected = value;
                    check("gearbox condition: native damage checks consume available value " + expected, () =>
                    { clean(); packet(state(expected, 0)); damageIs(expected); damageIs(expected); });
                }
                check("gearbox condition: absent reports retain native saved lookup", () =>
                { clean(); damageIs(3); });
                check("gearbox condition: host parked snapshot supplies known zero without claiming the vehicle", () =>
                {
                    clean(); Set(item, "RemoteOwner", (byte)255); var report = state(0, 65535); report.OwnerPlayerId = 0; packet(report); damageIs(0);
                    Require((byte)Get(item, "RemoteOwner") == 255 && (ushort)Get(item, "OutConditionSequence") == 0, "Snapshot changed ownership or live sequence.");
                });
                check("gearbox condition: missing damage bit and withdrawal resume native lookup", () =>
                {
                    clean(); packet(state(1, 0)); damageIs(1); var missing = state(0, 1); missing.Availability = VehicleCondition.AvailableFL; packet(missing); damageIs(3);
                    packet(state(2, 2)); damageIs(2); var withdraw = state(0, 3); withdraw.Availability = 0; packet(withdraw); damageIs(3);
                });
                check("gearbox condition: duplicate stale forged and wrong-owner packets cannot change native branch", () =>
                {
                    clean(); packet(state(1, 4)); packet(state(2, 4)); packet(state(0, 3)); var wrong = state(2, 5); wrong.OwnerPlayerId = 2; packet(wrong);
                    Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(state(2, 5)), Channel.ReliableOrdered); damageIs(1);
                });
                check("gearbox condition: local and seated preclaim drivers use their native inputs", () =>
                {
                    clean(); packet(state(1, 0)); Set(item, "LocallyOwned", true); damageIs(3);
                    Set(item, "LocallyOwned", false); player.SetParent(f.Body.transform, false); damageIs(3);
                });
                check("gearbox condition: host and disconnected sessions use native inputs", () =>
                {
                    clean(); packet(state(1, 0)); SetProperty(session, "IsHost", true); damageIs(3);
                    SetProperty(session, "IsHost", false); SetProperty(session, "State", SessionState.Idle); damageIs(3);
                });
                check("gearbox condition: owner changes cannot borrow a prior driver's damage", () =>
                {
                    clean(); packet(state(1, 0)); Set(item, "RemoteOwner", (byte)2); damageIs(3);
                    var fresh = state(2, 0); fresh.OwnerPlayerId = 2; packet(fresh); damageIs(2);
                });
                check("gearbox condition: approved final release retains the damage branch through former-driver cleanup", () =>
                {
                    clean(); packet(state(1, 0)); Call(itemsObject, "OnRemoteItemTransform", new ItemTransform { ItemId = VehicleId,
                        OwnerPlayerId = 1, Sequence = 10, Flags = ItemTransform.FlagFinal });
                    Require((byte)Get(item, "RemoteOwner") == 255 && Get(item, "ParkedVehicleCondition") != null, "Native final was not accepted.");
                    damageIs(1); Call(vehicles, "ForgetVehicleStatePlayer", (byte)1); damageIs(1);
                    var host = state(0, 65535); host.OwnerPlayerId = 0; packet(host); damageIs(0);
                    Require(Get(item, "ParkedVehicleCondition") == null, "Host correction did not retire the parked result.");
                });
                check("gearbox condition: unapproved release cannot project parked damage", () =>
                { clean(); packet(state(1, 0)); Set(item, "RemoteOwner", (byte)255); damageIs(3); });
                check("gearbox condition: replacement body and unregistering retire native readers", () =>
                {
                    clean(); packet(state(1, 0)); Set(item, "Body", Get(original, "Body")); damageIs(3); Set(item, "Body", f.Body);
                    items.Remove(VehicleId); damageIs(3); items[VehicleId] = item; damageIs(1);
                });
                check("gearbox condition: projection leaves native target and warmed cache unchanged", () =>
                {
                    clean(); damageIs(3); var target = Get(f.Reader, "gameObject"); var cache = Get(f.Reader, "fsm"); var previous = Get(f.Reader, "goLastFrame");
                    packet(state(1, 0)); damageIs(1);
                    Require(ReferenceEquals(target, Get(f.Reader, "gameObject")) && ReferenceEquals(cache, Get(f.Reader, "fsm"))
                        && ReferenceEquals(previous, Get(f.Reader, "goLastFrame")), "Projection rewrote native source/cache.");
                });
                check("gearbox condition: accepted read works with a missing local saved target without inventing a replacement", () =>
                {
                    clean(); packet(state(1, 0)); var source = f.Consumer.FsmVariables.FindFsmGameObject("db_Gearbox"); var previous = source.Value;
                    try { source.Value = null; damageIs(1); Require(source.Value == null, "Projection populated native source."); }
                    finally { source.Value = previous; }
                });
                check("gearbox condition: accepted-copy isolation and session teardown preserve saved damage", () =>
                {
                    clean(); var report = state(1, 0); Require((bool)Call(vehicles, "ApplyVehicleCondition", report)!, "Report rejected."); report.DrivetrainDamage = 2;
                    damageIs(1); Call(vehicles, "ClearConditionStreams"); damageIs(3); packet(state(0, 0)); damageIs(0);
                });
                check("gearbox condition: reverse failure still cannot write guest saved wear", () =>
                {
                    clean(); packet(state(1, 0)); damageIs(1); NativeBagPartChecks.Fire(f.Consumer, "Reverse");
                    Require(!f.Writer.Enabled && !NativeBagPartChecks.State(f.Consumer, "Reverse").ActiveActions.Contains(f.Writer), "Protected reverse writer was scheduled."); saved();
                });
                foreach (string change in new[] { "removed", "disabled" })
                    check("gearbox condition: " + change + " reader pauses admission and recovers after repair", () =>
                    {
                        clean(); packet(state(1, 0)); var readState = NativeBagPartChecks.State(f.Consumer, "Damage type");
                        try
                        {
                            if (change == "removed") { readState.Actions[0] = new GearboxConditionHold(); readState.Actions[0].Init(readState); }
                            else f.Reader.Enabled = false;
                            Require(!prepare() && !f.Consumer.enabled, "Incomplete reader silently admitted native saved input."); saved();
                        }
                        finally { readState.Actions[0] = f.Reader; f.Reader.Enabled = true; }
                        Require(prepare(), "Repaired gearbox reader did not prepare."); damageIs(1);
                    });
                foreach (string field in new[] { "storeValue", "variableName", "fsmName", "gameObject", "everyFrame" })
                    check("gearbox condition: changed " + field + " pauses only the damaged reader and recovers", () =>
                    {
                        clean(); packet(state(1, 0)); object previous = Get(f.Reader, field);
                        try
                        {
                            object changed = field == "storeValue" ? (object)f.SavedDamage : field == "everyFrame" ? true
                                : field == "gameObject" ? new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                                    GameObject = new FsmGameObject { Name = "db_Gearbox", UseVariable = true, Value = f.Source.gameObject } }
                                : (object)new FsmString("Other");
                            Set(f.Reader, field, changed); f.Reader.OnEnter();
                            Require(!f.Consumer.enabled && !f.Consumer.Fsm.RestartOnEnable, "Invalid read did not pause its consumer."); saved();
                            Require(!prepare() && !f.Consumer.enabled, "Preparation resumed an unrepaired reader.");
                        }
                        finally { Set(f.Reader, field, previous); }
                        Require(prepare(), "Restored reader did not recover."); damageIs(1);
                    });
                check("gearbox condition: moved native consumer rejects its old profile and recovers at the audited path", () =>
                {
                    clean(); packet(state(1, 0)); var originalParent = f.Consumer.transform.parent;
                    try
                    {
                        f.Consumer.transform.SetParent(f.Body.transform, false); f.Reader.OnEnter();
                        Require(!f.Consumer.enabled, "Moved consumer reused an obsolete native profile."); saved();
                    }
                    finally { f.Consumer.transform.SetParent(originalParent, false); }
                    Require(prepare(), "Restored consumer identity did not recover."); damageIs(1);
                });
                check("gearbox condition: canonical output aliased to global is rejected before overwrite", () =>
                {
                    clean(); packet(state(1, 0)); var globals = FsmVariables.GlobalVariables; var originalGlobals = globals.IntVariables;
                    var added = new FsmInt[originalGlobals.Length + 1]; Array.Copy(originalGlobals, added, originalGlobals.Length); added[originalGlobals.Length] = f.Damage;
                    try { globals.IntVariables = added; f.Damage.Value = 9; f.Reader.OnEnter(); Require(f.Damage.Value == 9 && !f.Consumer.enabled, "Read overwrote a global alias."); saved();
                        Require(!prepare() && !f.Consumer.enabled, "Preparation resumed an unrepaired global alias."); }
                    finally { globals.IntVariables = originalGlobals; }
                    Require(prepare(), "Global alias repair did not recover."); damageIs(1);
                });
                check("gearbox condition: canonical output aliased to source data is rejected before overwrite", () =>
                {
                    clean(); packet(state(1, 0)); var originalInts = f.Source.FsmVariables.IntVariables;
                    try
                    {
                        f.Source.FsmVariables.IntVariables = new[] { f.SavedDamage, f.Damage }; f.Damage.Value = 9;
                        f.Reader.OnEnter(); Require(f.Damage.Value == 9 && !f.Consumer.enabled, "Read overwrote a saved alias."); saved();
                        Require(!prepare() && !f.Consumer.enabled, "Preparation resumed an unrepaired saved alias.");
                    }
                    finally { f.Source.FsmVariables.IntVariables = originalInts; }
                    Require(prepare(), "Saved alias repair did not recover."); damageIs(1);
                });
            }
            finally { items[VehicleId] = original; player.SetParent(parent, false); player.position = position; reset(); }
        }

        private sealed class GearboxConditionFixture : IDisposable
        {
            internal readonly Rigidbody Body;
            internal readonly PlayMakerFSM Consumer, Source;
            internal readonly FsmInt Damage, SavedDamage;
            internal readonly FsmFloat Wear;
            internal readonly FsmStateAction Reader, Writer;
            internal GearboxConditionFixture()
            {
                var root = new GameObject("CORRIS"); Body = root.AddComponent<Rigidbody>(); Body.isKinematic = true; Body.useGravity = false;
                var row = (Dictionary<string, object>)ReadOccupancyRows("gearbox-condition-probe.json")[0];
                var current = root.transform; var path = ((string)row["path"]).Split('/');
                for (int i = 1; i < path.Length; i++) { var child = new GameObject(path[i]); child.transform.SetParent(current, false); current = child.transform; }
                Consumer = NativeBagPartChecks.MakeFsm(current.gameObject, row); Consumer.Fsm.Init(Consumer);
                Damage = Consumer.FsmVariables.FindFsmInt("DamageType");
                var saved = new GameObject("saved gearbox input"); saved.transform.SetParent(root.transform, false);
                Source = saved.AddComponent<PlayMakerFSM>(); Source.enabled = false; Set(Source, "fsm", new Fsm()); Source.Fsm.Name = "Data";
                SavedDamage = new FsmInt { Name = "DamageType", UseVariable = true, Value = 3 }; Wear = new FsmFloat { Name = "Wear", UseVariable = true, Value = 90 };
                Source.FsmVariables.IntVariables = new[] { SavedDamage }; Source.FsmVariables.FloatVariables = new[] { Wear };
                Source.Fsm.StartState = "Probe idle"; Source.Fsm.States = new[] { new FsmState(Source.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } };
                Consumer.FsmVariables.FindFsmGameObject("db_Gearbox").Value = saved;
                foreach (Dictionary<string, object> nativeState in (IEnumerable)row["states"])
                {
                    string name = (string)nativeState["name"]; var target = NativeBagPartChecks.State(Consumer, name); var raw = (List<object>)nativeState["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++)
                        actions[i] = name == "Damage type" || name == "Delay" || name == "Reverse" && i == 6
                            ? (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { (Dictionary<string, object>)raw[i], Consumer })
                            : new GearboxConditionHold();
                    target.Actions = actions; foreach (var action in actions) action.Init(target);
                }
                Reader = NativeBagPartChecks.State(Consumer, "Damage type").Actions[0]; Writer = NativeBagPartChecks.State(Consumer, "Reverse").Actions[6];
                NativeBagPartChecks.Start(Source); NativeBagPartChecks.Start(Consumer);
            }
            internal void Seed() { SavedDamage.Value = 3; Wear.Value = 90; Damage.Value = 9; NativeBagPartChecks.Fire(Consumer, "Probe idle"); }
            internal void Read() => NativeBagPartChecks.Fire(Consumer, "Damage type");
            public void Dispose() => UnityEngine.Object.DestroyImmediate(Body.gameObject);
        }
        private sealed class GearboxConditionHold : FsmStateAction { }
    }
}
