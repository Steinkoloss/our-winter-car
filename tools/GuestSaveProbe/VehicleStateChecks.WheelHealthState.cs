using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class VehicleStateChecks
    {
        private delegate bool WheelHealthRead(PlayMakerFSM fsm, int wheel, out float health, out bool ready);

        private static void MeasureWheelHealthWait(object vehicles, WheelHealthFixture fixture, string phase)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--wintermp-wheel-wait-performance") < 0) return;
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-performance-sandbox.txt")))
                throw new InvalidOperationException("Wheel wait timing requires a marked isolated game copy.");
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            var prepare = (Func<bool, bool>)Delegate.CreateDelegate(typeof(Func<bool, bool>), guard.GetMethod("Prepare", Static));
            var read = (WheelHealthRead)Delegate.CreateDelegate(typeof(WheelHealthRead), vehicles, Vehicles.GetMethod("TryReadHostWheelHealth", Members));
            var deadline = guard.GetField("_nextScanAt", Static); float previous = (float)deadline.GetValue(null);
            var rows = new List<string> { "objects,workload,samples,median_ms,p95_ms,max_ms,gen0_collections,heap_delta_bytes" };
            try
            {
                deadline.SetValue(null, float.MaxValue);
                PerformanceChecks.Measure(rows, 1000, "guest-wheel-" + phase + "-lookup", () =>
                { for (int i = 0; i < 1000; i++) for (int wheel = 0; wheel < 4; wheel++) read(fixture.Wheels[wheel], wheel, out _, out _); });
                PerformanceChecks.Measure(rows, 1000, "guest-wheel-" + phase + "-prepare", () =>
                { for (int i = 0; i < 1000; i++) prepare(false); });
            }
            finally { deadline.SetValue(null, previous); }
            File.WriteAllLines(Path.Combine(BepInEx.Paths.GameRootPath, "guest-save-probe/wheel-" + phase + "-performance.csv"), rows.ToArray());
        }

        private static void RunWheelHealthStateChecks(Action<string, Action> check, object original, object vehicles,
            SessionManager session, CaptureTransport transport, Action reset)
        {
            var items = (IDictionary)Get(Get(vehicles, "_items"), "_items"); uint id = StableHash.Fnv1a32("vehicle:CORRIS");
            var guard = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true);
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            var rule = catalog.GetProperty("VehicleWheelHealth", Static).GetValue(null, null);
            var policy = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true).GetField("Policy", Static).GetValue(null);
            var protection = policy.GetType().GetProperty("ProtectWorld", Members); bool wasProtected = (bool)protection.GetValue(policy, null);
            Action<bool> protect = value => protection.GetSetMethod(true).Invoke(policy, new object[] { value });
            Action prepare = () => guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true });
            const string prefix = "host wheel health: ";
            using (GuestEngineInputChecks.SuspendProjectionForFixture())
            try
            {
                reset(); protect(false);
                using (var f = new WheelHealthFixture(true))
                {
                    var item = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                    Set(item, "Id", id); Set(item, "Body", f.Body); Set(item, "Path", "CORRIS"); Set(item, "IsVehicle", true); items[id] = item;
                    Action<bool, byte> mode = (host, owner) =>
                    {
                        SetProperty(session, "IsHost", host); SetProperty(session, "State", host ? SessionState.Hosting : SessionState.Connected);
                        SetProperty(session, "LocalPlayerId", host ? (byte)0 : (byte)3); Set(item, "RemoteOwner", owner); Set(item, "LocallyOwned", false); protect(!host);
                    };
                    mode(true, 255); f.Seed();
                    Func<VehicleWheelHealthState> build = () => (VehicleWheelHealthState)Call(vehicles, "BuildVehicleWheelHealthState", item)!;
                    Func<VehicleWheelHealthState?> read = () => (VehicleWheelHealthState?)Call(vehicles, "ReadWheelHealthState", id);
                    Action poll = () => { Set(vehicles, "_nextWheelHealthPoll", 0f); Call(vehicles, "UpdateWheelHealthStates", session); };
                    Action<VehicleWheelHealthState> packet = value => Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(value), Channel.ReliableOrdered);
                    Func<VehicleWheelHealthState, bool> receive = value => (bool)Call(vehicles, "OnVehicleWheelHealthState", value)!;
                    Func<VehicleWheelHealthState> last = () =>
                    {
                        Require(transport.Packets.Count == session.PlayerCount, "Tyre health did not reach every guest.");
                        VehicleWheelHealthState? value = null;
                        foreach (var sent in transport.Packets)
                        {
                            Require(sent.Message is VehicleWheelHealthState && sent.Channel == Channel.ReliableOrdered, "Wrong tyre health message/channel.");
                            value = (VehicleWheelHealthState)PacketCodec.Decode(PacketCodec.Encode(sent.Message));
                        }
                        return value ?? throw new InvalidOperationException("No tyre health publication.");
                    };
                    Action saved = () => { for (int i = 0; i < 4; i++) Require(f.Saved[i].Value == 90 + i, "Guest health input changed a saved tyre."); };
                    VehicleWheelHealthState? sent = null;
                    check(prefix + "capture publishes exact native mounted inputs without reading driver scratch", () =>
                    {
                        for (int i = 0; i < 4; i++) { f.Saved[i].Value = 90.125f + i; f.Health[i].Value = 7; }
                        transport.Packets.Clear(); poll(); sent = last();
                        Require(sent.Availability == 15 && sent.Revision == 1 && sent.VehicleId == id, "Wrong source availability or initial revision.");
                        for (int i = 0; i < 4; i++) Require(sent.Health(i) == 90.125f + i && f.Health[i].Value == 7
                            && f.Saved[i].Value == 90.125f + i, "Snapshot sampled scratch or ran a native write/read.");
                    });
                    check(prefix + "unchanged publication stays quiet until keepalive", () =>
                    {
                        transport.Packets.Clear(); poll(); Require(transport.Packets.Count == 0, "Unchanged health flooded guests.");
                        Set(vehicles, "_nextWheelHealthKeepalive", 0f); poll(); Require(last().Revision == sent!.Revision, "Keepalive changed revision.");
                    });
                    foreach (string method in new[] { "BuildJoinVehicleSnapshots", "BuildVehicleResyncMessages", "BuildVehicleStateMessages" })
                        check(prefix + method + " includes current health and preserves pending publication", () =>
                        {
                            f.Saved[0].Value += .125f;
                            var values = (IEnumerable)Call(vehicles, method, method == "BuildVehicleStateMessages" ? new object[] { item, (byte)0 } : new object[0])!;
                            VehicleWheelHealthState? snapshot = null;
                            foreach (object value in values) if (value is VehicleWheelHealthState health) { snapshot = health; break; }
                            Require(snapshot != null && snapshot.HealthFL == f.Saved[0].Value && f.Health[0].Value == 7, "Snapshot missed source or ran a native action.");
                            transport.Packets.Clear(); poll(); sent = last(); Require(sent.Revision == snapshot!.Revision, "Snapshot consumed live publication.");
                        });
                    check(prefix + "parked repairs publish without borrowing an approved former driver condition", () =>
                    {
                        Set(item, "ParkedConditionBody", f.Body); Set(item, "ParkedVehicleCondition", new VehicleCondition { VehicleId = id,
                            OwnerPlayerId = 1, Availability = VehicleCondition.AvailableAll, HealthFL = 2 });
                        f.Saved[0].Value = 99.75f; transport.Packets.Clear(); poll(); sent = last();
                        Require(sent.HealthFL == 99.75f && sent.Revision > 1, "Former driver condition hid a host repair.");
                    });
                    check(prefix + "driver handoffs retain the host publication revision", () =>
                    {
                        uint revision = build().Revision;
                        foreach (byte owner in new byte[] { 1, 2, 255, 0 }) { mode(true, owner); Require(build().Revision == revision, "Ownership reset tyre health history."); }
                    });
                    foreach (int wheel in new[] { 0, 1, 2, 3 })
                    {
                        int index = wheel;
                        check(prefix + "nonfinite wheel " + index + " withdraws independently and repairs", () =>
                        {
                            float old = f.Saved[index].Value; f.Saved[index].Value = float.NaN;
                            try { var value = build(); Require(value.Availability == (15 & ~(1 << index)) && value.Health(index) == 0, "Bad wheel withdrew other wheels or leaked NaN."); }
                            finally { f.Saved[index].Value = old; }
                            Require(build().Availability == 15, "Repaired health stayed unavailable.");
                        });
                    }
                    check(prefix + "changed native reader cache withdraws only its wheel without mutation", () =>
                    {
                        var action = NativeBagPartChecks.State(f.Wheels[0], "State 1").Actions[12]; var cache = Get(action, "fsm");
                        Set(action, "fsm", f.Wheels[1].FsmVariables.FindFsmGameObject("ThisTire").Value.GetComponent<PlayMakerFSM>());
                        try { Require(build().Availability == 14, "Foreign cached native source was published."); }
                        finally { Set(action, "fsm", cache); }
                        Require(build().Availability == 15, "Repaired reader cache stayed unavailable.");
                    });
                    check(prefix + "renamed mount and duplicate health scalar fail closed and recover", () =>
                    {
                        var mount = f.Wheels[0].FsmVariables.FindFsmGameObject("ThisTire").Value; string name = mount.name; mount.name = "foreign mount";
                        try { Require(build().Availability == 14, "Renamed mount supplied host health."); } finally { mount.name = name; }
                        var data = mount.GetComponent<PlayMakerFSM>(); var scalars = data.FsmVariables.FloatVariables;
                        var duplicate = new FsmFloat[scalars.Length + 1]; Array.Copy(scalars, duplicate, scalars.Length);
                        duplicate[scalars.Length] = new FsmFloat { Name = "TireHealth", UseVariable = true, Value = 50 }; data.FsmVariables.FloatVariables = duplicate;
                        try { Require(build().Availability == 14, "Duplicate scalar supplied host health."); } finally { data.FsmVariables.FloatVariables = scalars; }
                        Require(build().Availability == 15, "Repaired mount remained unavailable.");
                    });
                    check(prefix + "disabled consumers withdraw without a snapshot enabling them", () =>
                    {
                        f.Wheels[0].enabled = false;
                        try { Require(build().Availability == 14 && !f.Wheels[0].enabled, "Read-only capture resumed a paused wheel."); }
                        finally { f.Wheels[0].enabled = true; }
                        Require(build().Availability == 15, "Live consumer did not recover.");
                    });
                    check(prefix + "retirement and replacement body preserve withdrawal revision history", () =>
                    {
                        transport.Packets.Clear(); poll(); uint before = build().Revision; items.Remove(id); transport.Packets.Clear();
                        try { poll(); var missing = last(); Require(missing.Availability == 0 && missing.Revision != before, "Removed car was not withdrawn."); }
                        finally { items[id] = item; }
                        transport.Packets.Clear(); poll(); Require(last().Availability == 15 && build().Revision != before, "Rediscovery lost current health.");
                        Set(item, "Body", Get(original, "Body")); Require(build().Availability == 0, "Replacement body exposed old mount values.");
                        Set(item, "Body", f.Body); Require(build().Availability == 15, "Original validated body did not recover.");
                    });

                    f.Seed(); mode(false, 1); Call(vehicles, "ClearWheelHealthStates");
                    check(prefix + "registered guest consumers wait for the first host result with saved writers blocked", () =>
                    {
                        prepare(); for (int i = 0; i < 4; i++)
                            Require(!f.Wheels[i].enabled && !NativeBagPartChecks.State(f.Wheels[i], "State 1").Actions[11].Enabled,
                                "Missing host input resumed a guest saved-health consumer."); saved();
                    });
                    check(prefix + "activation readiness accepts a contained input wait between scans", () =>
                    {
                        Require((bool)ActivationGuard().Invoke(null, new object[] { true })
                            && !(bool)guard.GetMethod("Prepare", Static).Invoke(null, new object[] { true }),
                            "Missing host input confused activation with simulation readiness.");
                        var deadline = guard.GetField("_nextScanAt", Static); float previous = (float)deadline.GetValue(null);
                        try
                        {
                            float future = Time.unscaledTime + 100f; deadline.SetValue(null, future);
                            Require((bool)ActivationGuard().Invoke(null, new object[] { false })
                                && (float)deadline.GetValue(null) == future, "Waiting activation forced a scan or stayed unavailable.");
                        }
                        finally { deadline.SetValue(null, previous); }
                        for (int i = 0; i < 4; i++) Require(!f.Wheels[i].enabled, "Readiness resumed a wheel without host health."); saved();
                    });
                    foreach (bool changed in new[] { false, true })
                    {
                        bool invalid = changed;
                        check(prefix + (invalid ? "changed native input blocks power despite a contained health wait" : "remote ON and OFF proceed while native tyre consumers stay paused"), () =>
                        {
                            var power = MakePower(f.Body.gameObject); power.Fsm.Init(power); NativeBagPartChecks.Start(power);
                            var readAction = NativeBagPartChecks.State(f.Wheels[0], "State 1").Actions[12];
                            var variable = (FsmString)Get(readAction, "variableName"); string old = variable.Value;
                            Set(item, "ElectricityPowerFsm", power); Set(item, "NextRemoteElectricsAttemptAt", 0f);
                            try
                            {
                                if (invalid) variable.Value = "foreign health";
                                CallStatic("ApplyRemoteElectricity", item, true);
                                if (invalid)
                                    Require(power.ActiveStateName == "OFF" && !(bool)Get(item, "HasRemoteElectricsState"),
                                        "Unvalidated input signature permitted native activation.");
                                else
                                {
                                    Require(power.ActiveStateName == "ON" && power.FsmVariables.FindFsmBool("ACC").Value
                                        && (bool)Get(item, "HasRemoteElectricsState"),
                                        "A safely paused tyre consumer blocked remote ON.");
                                    CallStatic("ApplyRemoteElectricity", item, false);
                                    Require(power.ActiveStateName == "OFF" && !power.FsmVariables.FindFsmBool("ACC").Value
                                        && !(bool)Get(item, "RemoteElectricsApplied"),
                                        "A safely paused tyre consumer blocked immediate OFF.");
                                }
                                for (int i = 0; i < 4; i++) Require(!f.Wheels[i].enabled, "Native power resumed unavailable tyre inputs."); saved();
                            }
                            finally
                            {
                                variable.Value = old; UnityEngine.Object.DestroyImmediate(power.gameObject);
                                Set(item, "ElectricityPowerFsm", null); Set(item, "HasRemoteElectricsState", false);
                                Set(item, "RemoteElectricsApplied", false); Set(item, "NextRemoteElectricsAttemptAt", 0f); prepare();
                            }
                        });
                    }
                    var incoming = new VehicleWheelHealthState { VehicleId = id, Revision = 10, Availability = 15,
                        HealthFL = .125f, HealthFR = 37.625f, HealthRL = 80.875f, HealthRR = 99.75f };
                    MeasureWheelHealthWait(vehicles, f, "waiting");
                    for (int i = 0; i < 4; i++)
                    {
                        int wheel = i;
                        check(prefix + "wheel " + wheel + " resumes native readers and grip arithmetic with exact host health", () =>
                        {
                            packet(incoming); prepare(); f.Tick();
                            Require(f.Wheels[wheel].enabled && f.Health[wheel].Value == incoming.Health(wheel), "Native wheel did not consume exact host input.");
                            Require(Math.Abs(f.Wheels[wheel].FsmVariables.FindFsmFloat("GripReduction").Value - (100f - incoming.Health(wheel)) / 1800f) < .00001f,
                                "Native grip arithmetic used guest health."); saved();
                        });
                    }
                    MeasureWheelHealthWait(vehicles, f, "ready");
                    check(prefix + "driver observer and parked consumers keep the same host input", () =>
                    {
                        foreach (byte owner in new byte[] { 1, 2, 255, 0 })
                            foreach (bool local in new[] { false, true })
                            {
                                mode(false, owner); Set(item, "LocallyOwned", local); f.Tick();
                                for (int i = 0; i < 4; i++) Require(f.Health[i].Value == incoming.Health(i), "Driver ownership changed authoritative tyre input.");
                            }
                        Set(item, "LocallyOwned", false); mode(false, 1); saved();
                    });
                    check(prefix + "flat native reader also uses host health without zeroing the saved tyre", () =>
                    {
                        NativeBagPartChecks.Fire(f.Wheels[0], "Sound"); f.Tick();
                        Require(f.Wheels[0].ActiveStateName == "Flat friction" && f.Health[0].Value == incoming.HealthFL, "Flat reader used local saved health."); saved();
                    });
                    check(prefix + "old driver condition health cannot replace native host health", () =>
                    {
                        var old = new VehicleCondition { VehicleId = id, OwnerPlayerId = 1, Sequence = 1,
                            Availability = VehicleCondition.AvailableAll, HealthFL = 99, HealthFR = 98, HealthRL = 97, HealthRR = 96 };
                        Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(old), Channel.ReliableOrdered); f.Tick();
                        for (int i = 0; i < 4; i++) Require(f.Health[i].Value == incoming.Health(i), "A driver's quantized report replaced host tyre input."); saved();
                    });
                    check(prefix + "forged stale conflicting and wrong-channel packets cannot replace the host result", () =>
                    {
                        var bad = incoming.Copy(); bad.Revision++; bad.HealthFR = 1;
                        Call(session, "OnPacketReceived", new PeerId(1001), PacketCodec.Encode(bad), Channel.ReliableOrdered);
                        Call(session, "OnPacketReceived", new PeerId(999), PacketCodec.Encode(bad), Channel.UnreliableSequenced);
                        bad.Revision = incoming.Revision; Require(!receive(bad), "Conflicting revision accepted."); bad.Revision--; Require(!receive(bad), "Stale revision accepted.");
                        f.Tick(); Require(read()!.SameHealth(incoming) && f.Health[1].Value == incoming.HealthFR, "Rejected input changed native health."); saved();
                    });
                    check(prefix + "independent withdrawal pauses only the missing wheel and fresh repair resumes it", () =>
                    {
                        var missing = incoming.Copy(); missing.Revision++; missing.Availability = 14; missing.HealthFL = 0; packet(missing);
                        Require(!f.Wheels[0].enabled && f.Wheels[1].enabled, "Single wheel withdrawal paused wrong consumers.");
                        Require(!receive(incoming), "Old snapshot revived withdrawn wheel."); incoming.Revision += 2; incoming.HealthFL = 0; packet(incoming); f.Tick();
                        Require(f.Wheels[0].enabled && f.Health[0].Value == 0, "Known zero did not restore native input."); saved();
                    });
                    check(prefix + "metadata outage retains newer host state while consumers wait for repair", () =>
                    {
                        SetStaticProperty(catalog, "VehicleWheelHealth", null);
                        try
                        {
                            prepare(); Require(!f.Wheels[1].enabled && read() == null, "Missing metadata resumed local saved health.");
                            incoming.Revision++; incoming.HealthFR = 62.375f; Require(receive(incoming), "Known vehicle lost host result during metadata outage.");
                        }
                        finally { SetStaticProperty(catalog, "VehicleWheelHealth", rule); }
                        prepare(); f.Tick(); Require(f.Wheels[1].enabled && f.Health[1].Value == 62.375f, "Metadata repair revived stale health."); saved();
                    });
                    check(prefix + "arrival before discovery and replacement preserves copied host results", () =>
                    {
                        items.Remove(id); incoming.Revision++; incoming.HealthRR = 57.875f;
                        try { Require(receive(incoming), "Pre-discovery host result lost."); } finally { items[id] = item; }
                        prepare(); f.Tick(); Require(f.Health[3].Value == 57.875f, "Rebinding lost retained health.");
                        read()!.HealthRR = 1; Require(read()!.HealthRR == 57.875f, "Reader mutated copied health."); saved();
                    });
                    check(prefix + "native saved source and warmed reader cache survive host projection", () =>
                    {
                        var action = NativeBagPartChecks.State(f.Wheels[1], "State 1").Actions[12]; var source = Get(action, "gameObject");
                        var cache = Get(action, "fsm"); var previous = Get(action, "goLastFrame"); f.Tick();
                        Require(ReferenceEquals(source, Get(action, "gameObject")) && ReferenceEquals(cache, Get(action, "fsm"))
                            && ReferenceEquals(previous, Get(action, "goLastFrame")), "Host projection rewired a native saved source."); saved();
                    });
                    check(prefix + "temporarily unregistered and replaced consumers cannot fall back to personal health", () =>
                    {
                        items.Remove(id);
                        try { f.Tick(); Require(!f.Wheels[1].enabled && f.Health[1].Value == incoming.HealthFR, "Unregistered wheel resumed personal health."); }
                        finally { items[id] = item; }
                        prepare(); f.Tick(); Require(f.Wheels[1].enabled, "Rediscovered consumer did not resume.");
                        Set(item, "Body", Get(original, "Body"));
                        try { f.Tick(); Require(!f.Wheels[1].enabled && f.Health[1].Value == incoming.HealthFR, "Old body resumed personal health."); }
                        finally { Set(item, "Body", f.Body); }
                        prepare(); f.Tick(); Require(f.Wheels[1].enabled && f.Health[1].Value == incoming.HealthFR, "Restored body lost host input."); saved();
                    });
                    check(prefix + "disconnect and teardown retire health; solo readers retain native values", () =>
                    {
                        SetProperty(session, "State", SessionState.Idle); Require(!receive(incoming) && read() == null, "Disconnected state remained active.");
                        Call(vehicles, "ClearVehicleStateStreams"); mode(false, 1); Require(read() == null, "New session inherited health.");
                        var first = incoming.Copy(); first.Revision = 0; Require(receive(first), "New session rejected initial revision zero.");
                        mode(true, 255); f.Seed(); f.Tick(); saved(); Require(f.Health[1].Value == 91 && build().Revision == 1, "Solo read or new host publication used guest history.");
                        Require(!receive(first), "Host accepted a guest health result.");
                    });
                }
            }
            finally
            {
                SetStaticProperty(catalog, "VehicleWheelHealth", rule); items.Remove(id); protect(wasProtected); reset();
            }
        }
    }
}
