using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
        private static readonly Type World = Core.GetType("WinterMP.Core.Sync.WorldSyncManager", true);

        internal static IDisposable SuspendProjectionForFixture() => new InputScope();

        private sealed class InputScope : IDisposable
        {
            private readonly PropertyInfo _profile;
            private readonly FieldInfo _callback;
            private readonly object _savedProfile;
            private readonly object? _savedCallback;
            internal InputScope()
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
                _profile = catalog.GetProperty("GuestEngineInputs", Static);
                _callback = Core.GetType("WinterMP.Core.Sync.GuestEngineProtection", true).GetField("PrepareInputs", Static);
                _savedProfile = _profile.GetValue(null, null); _savedCallback = _callback.GetValue(null);
                RestoreOwner();
                // RPM-only fixtures deliberately omit part factories and readers.
                // Scope projection out while retaining their real writer guards.
                _profile.GetSetMethod(true).Invoke(null, new[] { Activator.CreateInstance(_savedProfile.GetType(), true) });
            }
            private void RestoreOwner()
            {
                var owner = (_callback.GetValue(null) as Delegate)?.Target;
                if (owner != null) Call(owner, "RestoreGuestEngineInputs");
            }
            public void Dispose()
            {
                RestoreOwner();
                _profile.GetSetMethod(true).Invoke(null, new[] { _savedProfile });
                _callback.SetValue(null, _savedCallback);
            }
        }

        internal static void Run(Action<string, Action> check)
        {
            using (var f = new Fixture())
            {
                check("engine inputs: warmed native readers initially follow the saved distributor mount", () =>
                {
                    f.ReadAll(); Require(f.Installed && f.Angle == 2 && f.Tightness == 8 && f.Wear == 77,
                        "Native cache baseline did not read the retained saved mount.");
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Mount.gameObject),
                        "Native reader was not warmed before projection.");
                });
                check("engine inputs: no host part projects absent despite a fitted saved distributor", () =>
                {
                    Require(f.Prepare(), "Missing host part prevented safe input binding."); f.ReadAll();
                    Require(!f.Installed && f.Proxy != f.Mount.gameObject && f.Proxy != f.Original.gameObject,
                        "Missing host state borrowed the saved distributor."); f.AssertSaved();
                });
                check("engine inputs: only the four reader owner wrappers move to isolated proxy Data", () =>
                {
                    var proxy = f.Proxy;
                    foreach (var action in f.Readers) Require(f.Target(action) == proxy, "Readers did not share one proxy.");
                    Require(f.Shared.Value == f.Mount.gameObject && f.Target(f.Action("Random move", 0)) == f.Mount.gameObject,
                        "Projection rewired the shared native mount variable or a guarded writer path.");
                    var data = f.ProxyData;
                    Require(data.FsmName == "Data" && data.FsmVariables.FindFsmBool("Installed") != null
                        && data.FsmVariables.FindFsmFloat("Wear") != null && data.FsmVariables.FindFsmFloat("Tightness") != null
                        && data.FsmVariables.FindFsmFloat("SparkAngle") != null && !data.enabled,
                        "Projection did not create complete inert input Data.");
                    Require(proxy.GetComponent<Rigidbody>() == null, "Input proxy acquired physical simulation.");
                });
                check("engine inputs: unapplied host snapshot stays absent until its attachment is ready", () =>
                {
                    f.Receive(90, 8, 12, false); Require(f.Prepare(), "Accepted pending state broke preparation."); f.ReadAll();
                    Require(!f.Installed, "Pending materialization supplied engine inputs.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied state did not prepare."); f.ReadAll();
                    Require(f.Installed && f.Angle == 12 && f.Tightness == 8 && f.Wear == 90,
                        "Applied host distributor did not supply native readers."); f.AssertSaved();
                });
                check("engine inputs: warmed original caches switch to the host proxy", () =>
                {
                    foreach (var action in f.Readers) Require(ReferenceEquals(Get(action, "goLastFrame"), f.Proxy),
                        "A warmed GetFsm cache retained the saved mount.");
                    f.Fire("Ignition"); Require(f.Reader.ActiveStateName == "Plug data" && f.Installed,
                        "Actual native ignition gate ignored the host Installed input.");
                });
                check("engine inputs: actual native timing arithmetic uses host timing and local boost", () =>
                {
                    f.Boost.FsmVariables.FindFsmFloat("SparkRetard").Value = 1;
                    f.Fire("Spark angle?"); Require(f.Reader.ActiveStateName == "Limiter" && Near(f.Angle, 13)
                        && Near(f.Reader.FsmVariables.FindFsmFloat("SparkTimingMultiplier").Value, 13f / 15),
                        "Native timing calculation did not combine host timing with its normal boost input.");
                });
                check("engine inputs: updating the same proxy preserves native scratch calculations", () =>
                {
                    var proxy = f.Proxy; float angle = f.Angle; f.Receive(91, 8, 9, true); Require(f.Prepare(), "Update failed.");
                    Require(f.Proxy == proxy && f.Angle == angle, "Projection recreated its proxy or overwrote shared native scratch.");
                    f.Fire("Spark angle?"); Require(Near(f.Angle, 10) && Near(f.Reader.FsmVariables.FindFsmFloat("SparkTimingMultiplier").Value, 10f / 15),
                        "Next real reader entry did not observe updated proxy data."); f.Boost.FsmVariables.FindFsmFloat("SparkRetard").Value = 0;
                });
                check("engine inputs: newer accepted bolt tightness takes precedence over part snapshot", () =>
                {
                    Call(f.Bridge, "ObserveReplacementTightness", f.PartId, 4f); Require(f.Prepare(), "Bolt receipt preparation failed.");
                    f.Readers[2].OnEnter(); Require(f.Tightness == 4, "Projection ignored a newer accepted bolt receipt.");
                    Call(f.Bridge, "ObserveReplacementTightness", f.PartId, 8f);
                });
                check("engine inputs: native loose-distributor calculation cannot mutate saved timing or mesh", () =>
                {
                    f.Receive(90, 3, 9, true); Require(f.Prepare(), "Loose-bolt input preparation failed.");
                    f.Fire("Distributor tight?"); Require(f.Reader.ActiveStateName == "Limiter" && f.Tightness == 3,
                        "Native loose-distributor branch did not run."); f.AssertSaved();
                });
                check("engine inputs: host condition drives the native damaged distributor branch", () =>
                {
                    f.Receive(3, 8, 9, true); Require(f.Prepare(), "Damaged input preparation failed."); f.Fire("Damage?");
                    Require(f.Wear == 3 && (f.Reader.ActiveStateName == "Retarded" || f.Reader.ActiveStateName == "Advanced"),
                        "Native damage choice still used the guest's healthy saved part."); f.AssertSaved();
                });
                foreach (string change in new[] { "pending", "revision", "hidden", "foreign parent", "inactive part", "changed native ID", "failed factory" })
                {
                    string scenario = change;
                    check("engine inputs: " + scenario + " cannot retain an installed input", () => f.CheckUnavailable(scenario));
                }
                check("engine inputs: reparenting the native block preserves its relative mount identity", () =>
                {
                    var parent = f.Anchor.transform.parent; f.Anchor.transform.SetParent(f.Car.transform, false);
                    try { f.Receive(90, 8, 11, true); Require(f.Prepare(), "Moved native block rejected."); f.ReadAll(); Require(f.Installed && f.Angle == 11, "Asset-time absolute mount path leaked into runtime binding."); }
                    finally { f.Anchor.transform.SetParent(parent, false); }
                });
                check("engine inputs: pending second host occupant blocks the previously applied distributor", () =>
                {
                    var state = ((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(f.PartId)!;
                    state.NativeId = "VIN1318"; state.Revision = 1; PartIdentity.TryItemId(state.NativeId, out uint other);
                    Call(f.Sync, "OnReplacementPartState", state);
                    try
                    {
                        Require(((ReplacementPartReplica)Get(f.Sync, "_replacementReplica")).Get(other) != null, "Conflicting host fixture was rejected before projection.");
                        Require(f.Prepare(), "Conflicting occupancy broke safe preparation."); f.ReadAll();
                        Require(!f.Installed, "A pending second occupant left old applied engine inputs available.");
                    }
                    finally { ((ItemSpawnLifecycle)Get(f.Sync, "_spawnLifecycle")).Retire(other); ((HashSet<uint>)Get(f.Sync, "_pendingReplacements")).Remove(other); f.Prepare(); }
                });
                check("engine inputs: accepted removal clears installed input before old native view changes", () =>
                {
                    f.Receive(90, 0, 11, false, false); Require(f.Prepare(), "Removal preparation failed."); f.ReadAll();
                    Require(!f.Installed && (bool)Get(f.Binding, "FittedPresentation") && f.Part.transform.parent == f.Mount.transform,
                        "Removal input waited for the old fitted replica to move."); f.AssertSaved();
                });
                check("engine inputs: native absent-part ignition stops without synthetic restart", () =>
                {
                    f.Fire("Ignition"); Require(f.Reader.ActiveStateName == "Not Ok" && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value,
                        "Native missing-distributor gate did not request shutdown.");
                    f.Receive(90, 8, 11, true); Require(f.Prepare(), "Replacement input failed.");
                    Require(f.Reader.ActiveStateName == "Not Ok", "Host input forced a native ignition replay.");
                    f.Fire("Ignition"); Require(f.Reader.ActiveStateName == "Plug data", "Normal ignition restart did not read the available host part.");
                });
                foreach (string field in new[] { "variableName", "fsmName", "storeValue", "everyFrame" })
                {
                    string scenario = field;
                    check("engine inputs: changed native " + scenario + " pauses only the reader graph and recovers", () => f.CheckChanged(scenario));
                }
                CheckRepeatedValidation(f, check);
                CheckInputReadBoundary(f, check);
                check("engine inputs: replaced native reader action rebinds before reading saved state", () =>
                {
                    var state = NativeBagPartChecks.State(f.Reader, "Spark angle?"); var prior = state.Actions[1];
                    var replacement = f.Import("Spark angle?", 1); state.Actions[1] = replacement; replacement.Init(state);
                    try
                    {
                        f.Readers[1] = replacement; Require(f.Prepare(), "Reinitialized native action did not rebind.");
                        replacement.OnEnter(); Require(f.Angle == 11 && f.Target(replacement) == f.Proxy,
                            "New native cache read the retained original.");
                    }
                    finally { state.Actions[1] = prior; f.Readers[1] = prior; f.Prepare(); }
                });
                check("engine inputs: destroyed proxy is rebuilt without leaving stale cached Data", () =>
                {
                    var proxy = f.Proxy; UnityEngine.Object.DestroyImmediate(proxy); Require(f.Prepare(), "Destroyed proxy did not recover.");
                    f.ReadAll(); Require(f.Proxy != null && f.Proxy != proxy && f.Installed && f.Angle == 11,
                        "Rebuilt input object retained a destroyed native cache.");
                });
                check("engine inputs: changed owner prevents unsafe proxy destruction during cleanup", () =>
                {
                    var action = f.Readers[1]; var target = Get(action, "gameObject"); var proxy = f.Proxy;
                    Set(action, "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = proxy } });
                    try
                    {
                        Call(f.Sync, "RestoreGuestEngineInputs");
                        Require(!f.Reader.enabled && ((IList)Get(f.Sync, "_guestEngineInputs")).Count == 1 && proxy != null,
                            "Changed reader wrapper allowed a referenced proxy to be abandoned."); f.AssertSaved();
                        Require(!f.Prepare() && !f.Reader.enabled, "Unrepaired cleanup resumed the native reader graph.");
                    }
                    finally
                    {
                        Set(action, "gameObject", target); Call(f.Sync, "RestoreGuestEngineInputs");
                        Require(f.Prepare() && f.Reader.enabled, "Repaired cleanup failed to restore the native enabled state.");
                    }
                });
                check("engine inputs: disconnect preserves saved data and never falls back to it", () =>
                {
                    f.ReadAll(); Require(f.Installed, "Disconnect baseline lacked a live host input.");
                    Property(f.Session, "State", SessionState.Idle);
                    try
                    {
                        Require(f.Prepare(), "Protected disconnect preparation failed."); f.ReadAll();
                        Require(!f.Installed, "Disconnect retained host input or restored saved distributor input."); f.AssertSaved();
                    }
                    finally { Property(f.Session, "State", SessionState.Connected); f.Prepare(); }
                });
                check("engine inputs: retired host identity cannot borrow a saved installed part", () =>
                {
                    ((ItemSpawnLifecycle)Get(f.Sync, "_spawnLifecycle")).Retire(f.PartId); Require(f.Prepare(), "Retired input preparation failed.");
                    f.ReadAll(); Require(!f.Installed, "Retired host part still supplied engine inputs."); f.AssertSaved();
                });
                check("engine inputs: cleanup restores original reader wrappers before removing proxies", () =>
                {
                    var proxy = f.Proxy; Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]),
                        "Cleanup did not restore the original action-local owner wrapper.");
                    foreach (var action in f.Readers) Require(f.Target(action) != proxy, "Cleanup retained a reader into a pending-destroy proxy.");
                    f.ReadAll(); Require(f.Installed && f.Angle == 2 && f.Wear == 77 && f.Tightness == 8,
                        "Restored native readers retained proxy cache data."); f.AssertSaved();
                });
                check("engine inputs: destroyed consumer prunes its owned projection binding", () =>
                {
                    Property(f.Session, "State", SessionState.Connected); Require(f.Prepare(), "Final fixture rebind failed.");
                    UnityEngine.Object.DestroyImmediate(f.Reader); Require(f.Prepare(), "Destroyed consumer prevented protection readiness.");
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 0, "Destroyed native graph retained its input binding."); f.AssertSaved();
                });
                check("engine inputs: failed original restoration survives repeated clear and a new consumer", () =>
                {
                    Set(f.Sync, "_guestEngineInputOriginalsReady", false);
                    try
                    {
                        Call(f.Sync, "ClearReplacementParts"); Call(f.Sync, "ClearReplacementParts");
                        Require(!(bool)Get(f.Sync, "_guestEngineInputOriginalsReady"), "An empty second cleanup erased the failed-original latch.");
                        f.RecreateBlockedReader();
                        Require(!f.Prepare() && !f.Reader.enabled, "A recreated consumer bypassed failed original restoration.");
                        try { Call(f.Sync, "PrepareGuestEngineInputFsm", f.Reader); }
                        catch (TargetInvocationException error)
                        {
                            Require(error.InnerException is InvalidOperationException && error.InnerException.Message.Contains("Saved guest parts"),
                                "New consumer failed for an unrelated fixture mismatch."); return;
                        }
                        throw new InvalidOperationException("Failed original restoration allowed new native consumer inputs.");
                    }
                    finally { Set(f.Sync, "_guestEngineInputOriginalsReady", true); Call(f.Sync, "RestoreGuestEngineInputs"); }
                });
            }
        }

        private static void CheckRepeatedValidation(Fixture f, Action<string, Action> check)
        {
            CheckReaderPathValidation(f, check);
            foreach (string field in new[] { "fsmName", "variableName" })
            {
                string name = field;
                check("engine inputs: in-place " + name + " edits still pause the reader and recover", () =>
                {
                    var value = (FsmString)Get(f.Readers[1], name); string saved = value.Value;
                    value.Value = "Changed input";
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed live field escaped validation."); f.AssertSaved(); }
                    finally { value.Value = saved; Require(f.Prepare() && f.Reader.enabled, "Repaired live field did not recover."); }
                });
            }
            var binding = ((IList)Get(f.Sync, "_guestEngineInputs"))[0];
            var readers = (IList)Get(Get(binding, "Rule"), "Readers");
            check("engine inputs: repeated validation deduplicates the current reader variables", () =>
            {
                readers.Add(readers[0]);
                try { for (int i = 0; i < 10; i++) ValidateBoundInput(f.Sync, binding); }
                finally { readers.RemoveAt(readers.Count - 1); }
                ValidateBoundInput(f.Sync, binding); f.AssertSaved();
            });
            check("engine inputs: catalog variable growth is rejected and repair validates the same binding", () =>
            {
                var added = Activator.CreateInstance(readers[0].GetType(), true);
                Set(added, "Variable", "Unexpected input"); Set(added, "ActionType", "GetFsmFloat"); readers.Add(added);
                try { RequireInputValidationFailure(f, binding); }
                finally { readers.Remove(added); }
                ValidateBoundInput(f.Sync, binding); f.AssertSaved();
            });
            check("engine inputs: extra proxy variables are rejected and repair validates the same binding", () =>
            {
                var data = (PlayMakerFSM)Get(binding, "Data"); var saved = data.FsmVariables.FloatVariables;
                var changed = new List<FsmFloat>(saved) { new FsmFloat { Name = "Unexpected input" } };
                data.FsmVariables.FloatVariables = changed.ToArray();
                try { RequireInputValidationFailure(f, binding); }
                finally { data.FsmVariables.FloatVariables = saved; }
                ValidateBoundInput(f.Sync, binding); f.AssertSaved();
            });
        }

        private static void RequireInputValidationFailure(Fixture fixture, object binding)
        {
            try { ValidateBoundInput(fixture.Sync, binding); }
            catch (TargetInvocationException error)
            {
                Require(error.InnerException is InvalidOperationException, "Validation failed for an unrelated reason.");
                return;
            }
            throw new InvalidOperationException("Changed input unexpectedly passed validation.");
        }

        internal static void RunStarter(Action<string, Action> check)
        {
            using (var f = new Fixture("VIN130", "Starter"))
            {
                check("starter inputs: native readers warm against a different saved starter", () =>
                {
                    f.ReadAll(); Require(f.Installed && f.Wear == 77 && f.Durability == 2, "Saved starter baseline changed.");
                    foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Mount.gameObject), "Native cache was not warmed.");
                });
                check("starter inputs: cold host state prevents starting with the saved starter", () =>
                {
                    Require(f.Prepare(), "Starter proxy did not bind."); f.Fire("Wiring");
                    Require(!f.Installed && f.Reader.ActiveStateName == "Wait", "Wiring gate borrowed the saved installed starter.");
                    Require(f.ProxyData.FsmVariables.FloatVariables.Length == 2
                        && f.ProxyData.FsmVariables.FindFsmFloat("Tightness") == null, "Starter proxy retained distributor fields."); f.AssertSaved();
                });
                check("starter inputs: pending host starter stays absent until its revision applies", () =>
                {
                    f.Receive(90, 8, .7f, false); Require(f.Prepare(), "Pending state broke preparation."); f.ReadAll(); Require(!f.Installed, "Pending starter could start.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied starter did not prepare."); f.ReadAll();
                    Require(f.Installed && f.Wear == 90 && Near(f.Durability, .7f), "Host starter condition did not replace cached saved input.");
                });
                check("starter inputs: healthy host starter passes actual wiring and damage gates", () =>
                {
                    f.Fire("Wiring"); Require(f.Installed && f.Reader.ActiveStateName == "Check Flywheel", "Healthy starter did not pass native wear comparison.");
                    foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Proxy), "Native cache retained the saved mount.");
                    Require(f.Shared.Value == f.Mount.gameObject && f.Target(f.Action("Fuel Mixture", 11)) == f.Mount.gameObject,
                        "Read projection also retargeted shared native writes."); f.AssertSaved();
                });
                CheckStarterInputHooks(check, f);
                check("starter inputs: native wear calculation uses host durability without writing saved wear", () =>
                {
                    f.Fire("Fuel Mixture"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("StarterWear").Value, .197f * .7f), "Native durability calculation ignored host data.");
                    f.Action("Fuel Mixture", 11).OnUpdate(); f.Reader.Fsm.Update(); f.AssertSaved();
                });
                check("starter inputs: new durability waits for the normal attempt without overwriting scratch", () =>
                {
                    var proxy = f.Proxy; float current = f.Durability; f.Receive(90, 8, .4f, true); Require(f.Prepare(), "Durability update failed.");
                    Require(f.Proxy == proxy && f.Durability == current, "Update overwrote in-progress native scratch or rebuilt stable proxy.");
                    f.Fire("Starter damage"); Require(Near(f.Durability, .4f), "Next native reader entry missed new durability.");
                });
                foreach (float wear in new[] { 25f, 24f, 26f })
                {
                    float value = wear;
                    check("starter inputs: host wear " + value + " follows the native threshold", () =>
                    {
                        f.Receive(value, 8, .7f, true); Require(f.Prepare(), "Wear update failed."); f.Fire("Starter damage");
                        Require(f.Reader.ActiveStateName == (value > 25 ? "Check Flywheel" : "State 1"), "Native worn-starter branch diverged."); f.AssertSaved();
                    });
                }
                check("starter inputs: host publication appends the actual part durability", () =>
                {
                    Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false);
                    f.Part.FsmVariables.FindFsmFloat("Durability").Value = .73f;
                    try
                    {
                        var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                        Require(state != null && state.Scalars.Length == 3 && Near(state.Scalars[2], .73f), "Host publication omitted actual Durability.");
                        var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                        Require(Near(decoded.Scalars[2], .73f), "Durability was lost on the wire.");
                    }
                    finally { Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                    f.AssertSaved();
                });
                check("starter inputs: removal closes the wiring gate before old presentation moves", () =>
                {
                    f.Receive(90, 0, .7f, false, false); Require(f.Prepare(), "Removal preparation failed."); f.Fire("Wiring");
                    Require(!f.Installed && f.Reader.ActiveStateName == "Wait" && f.Part.transform.parent == f.Mount.transform,
                        "Old fitted replica kept a removed starter available."); f.AssertSaved();
                });
                check("starter inputs: refitting does not invent an ignition attempt", () =>
                {
                    f.Receive(90, 8, .7f, true); Require(f.Prepare(), "Refit failed.");
                    Require(f.Reader.ActiveStateName == "Wait", "Refit injected an ignition replay.");
                    f.Fire("Wiring"); Require(f.Reader.ActiveStateName == "Check Flywheel", "Normal attempt missed refitted starter.");
                });
                check("starter inputs: changed durability reader pauses and repairs the consumer", () =>
                {
                    var reader = f.Readers[2]; var original = Get(reader, "variableName");
                    Set(reader, "variableName", new FsmString { Value = "Wrong" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Changed native reader escaped protection."); f.AssertSaved(); }
                    finally { Set(reader, "variableName", original); f.Prepare(); }
                    Require(f.Reader.enabled, "Corrected starter remained paused."); f.ReadAll(); Require(f.Installed && Near(f.Durability, .7f), "Repaired starter returned to saved input.");
                });
                check("starter inputs: replaced proxy Data invalidates warmed native caches", () =>
                {
                    var previous = f.Proxy; UnityEngine.Object.DestroyImmediate(f.ProxyData); Require(f.Prepare(), "Missing proxy Data did not recover.");
                    f.ReadAll(); Require(f.Proxy != previous && Near(f.Durability, .7f), "A same-object stale Data cache survived rebuild.");
                });
                check("starter inputs: block movement preserves the accepted mount address", () =>
                {
                    f.Anchor.transform.SetParent(f.Car.transform, false); Require(f.Prepare(), "Reparented block failed."); f.ReadAll();
                    Require(f.Installed && Near(f.Durability, .7f), "Starter used the old absolute asset path.");
                });
                check("starter inputs: disconnect neutralizes an installed host starter", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Disconnect failed."); f.ReadAll();
                    Require(!f.Installed && f.Wear == 0 && f.Durability == 0, "Disconnected guest retained host starter input."); f.AssertSaved();
                });
                check("starter inputs: cleanup restores the original reader targets and caches", () =>
                {
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Original owner wrapper was lost.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 77 && f.Durability == 2, "Restored reader retained host proxy cache."); f.AssertSaved();
                });
            }
        }

        private static void CheckStarterInputHooks(Action<string, Action> check, Fixture f)
        {
            var originals = new Dictionary<FsmState, FsmStateAction[]>();
            var hook = Core.GetType("WinterMP.Core.Sync.FsmHook", true).GetMethod("OnStateEnter", Static, null,
                new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null);
            try
            {
                check("starter inputs: observation hooks preserve warmed host projection and cold rebinding", () =>
                {
                    foreach (var reader in f.Readers)
                    {
                        var state = reader.State;
                        if (originals.ContainsKey(state)) continue;
                        originals.Add(state, state.Actions);
                        Require((bool)hook.Invoke(null, new object[] { f.Reader, state.Name, (Action)(() => { }) }), "Hook installation failed.");
                    }
                    var proxy = f.Proxy;
                    Require(f.Prepare() && f.Proxy == proxy, "Leading hooks discarded warmed input projection.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 90 && Near(f.Durability, .7f), "Hooked readers lost host input.");
                    Call(f.Sync, "RestoreGuestEngineInputs"); Require(f.Prepare(), "Hooked native inputs did not bind cold.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 90 && Near(f.Durability, .7f), "Cold hooked readers borrowed saved input.");
                    f.AssertSaved();
                });
            }
            finally
            {
                foreach (var pair in originals) pair.Key.Actions = pair.Value;
                f.Prepare();
            }
        }

        internal static void RunWaterpump(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Oil", "Cooling" })
            using (var f = new Fixture("VIN126", consumer))
            {
                bool cooling = consumer == "Cooling";
                string label = "water pump " + consumer + ": ";
                check(label + "warmed native readers initially follow the saved pump", () =>
                {
                    f.ReadAll(); Require(f.Installed && f.Wear == 77 && (cooling ? f.Efficiency == 3 : f.Durability == 2), "Saved input baseline failed.");
                    foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Mount.gameObject), "Reader cache was not warmed.");
                });
                check(label + "missing and pending host pump cannot borrow the fitted saved pump", () =>
                {
                    Require(f.Prepare(), "Cold preparation failed."); f.ReadAll(); Require(!f.Installed && f.Wear == 0, "Saved pump leaked into guest inputs.");
                    f.Receive(90, 32, .7f, false); Require(f.Prepare(), "Pending preparation failed."); f.ReadAll(); Require(!f.Installed, "Pending pump supplied inputs.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied pump failed."); f.ReadAll();
                    Require(f.Installed && f.Wear == 90 && (cooling ? Near(f.Efficiency, 1.8f) : Near(f.Durability, .7f)), "Host pump values did not reach native readers."); f.AssertSaved();
                });
                check(label + "only selected read targets move to an inert family proxy", () =>
                {
                    foreach (var reader in f.Readers) Require(f.Target(reader) == f.Proxy && ReferenceEquals(Get(reader, "goLastFrame"), f.Proxy), "Native cache retained the saved pump.");
                    Require(!f.ProxyData.enabled && f.Proxy.GetComponent<Rigidbody>() == null && f.Shared.Value == f.Mount.gameObject, "Proxy changed native simulation ownership.");
                    Require(f.ProxyData.FsmVariables.FloatVariables.Length == (cooling ? 3 : 2), "Proxy exposes unrelated inputs.");
                    if (!cooling) Require(f.Target(f.Action("Wearing 2", 4)) == f.Mount.gameObject && !f.Action("Wearing 2", 4).Enabled, "Projection retargeted or enabled native saved wear.");
                    f.AssertSaved();
                });
                check(label + "updated values wait for native reads and preserve calculation scratch", () =>
                {
                    var proxy = f.Proxy; f.Receive(91, 32, .4f, true, efficiency: 2.4f); Require(f.Prepare(), "Input update failed.");
                    Require(f.Proxy == proxy && f.Wear == 90 && (cooling ? Near(f.Efficiency, 1.8f) : Near(f.Durability, .7f)), "Update replayed native reads or rebuilt its stable proxy.");
                    f.ReadAll(); Require(f.Wear == 91 && (cooling ? Near(f.Efficiency, 2.4f) : Near(f.Durability, .4f)), "Next native read missed the new host values."); f.AssertSaved();
                });
                if (cooling)
                {
                    foreach (float wear in new[] { 6f, 7f, 8f })
                    {
                        float value = wear;
                        check(label + "wear " + value + " follows the native circulation threshold", () =>
                        {
                            f.Receive(value, 32, .7f, true, efficiency: 2.4f); Require(f.Prepare(), "Wear update failed.");
                            f.PumpRpm = 1000; f.Fire("Water Pump 2");
                            Require(Near(f.Reader.FsmVariables.FindFsmFloat("Circulation").Value, value < 7 ? .0001f : 2.4f), "Native circulation threshold ignored host wear or efficiency."); f.AssertSaved();
                        });
                    }
                    check(label + "native belt and RPM gates still control circulation", () =>
                    {
                        f.PumpRpm = 99; f.Fire("Water Pump 2");
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("Circulation").Value, .0001f), "Projection bypassed the native stopped-engine gate.");
                        var belt = f.Reader.FsmVariables.FindFsmGameObject("db_FanBelt"); var original = belt.Value;
                        var absent = Data(Child(f.Extras, "absent belt"), null, 0); belt.Value = absent.gameObject;
                        try { f.PumpRpm = 1000; f.Fire("Water Pump 2"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("Circulation").Value, .0001f), "Projection bypassed the native absent-belt gate."); }
                        finally { belt.Value = original; UnityEngine.Object.DestroyImmediate(absent.gameObject); }
                    });
                    check(label + "latest bolt receipt drives the native loose-pump leak calculation", () =>
                    {
                        f.Receive(90, 32, .7f, true); Call(f.Bridge, "ObserveReplacementTightness", f.PartId, 4f); Require(f.Prepare(), "Bolt receipt failed.");
                        f.Reader.FsmVariables.FindFsmFloat("WaterLeakRate").Value = 0; f.Fire("Pump tightness");
                        Require(f.Tightness == 4 && Near(f.Reader.FsmVariables.FindFsmFloat("WaterLeakRate").Value, .05f / 4), "Native leak calculation ignored latest accepted bolts.");
                        Call(f.Bridge, "ObserveReplacementTightness", f.PartId, 32f); f.Prepare(); f.Reader.FsmVariables.FindFsmFloat("WaterLeakRate").Value = 0; f.Fire("Pump tightness");
                        Require(f.Reader.ActiveStateName == "Hoses" && f.Reader.FsmVariables.FindFsmFloat("WaterLeakRate").Value == 0, "Tight pump still took the loose leak branch."); f.AssertSaved();
                    });
                }
                else
                {
                    foreach (float wear in new[] { 4f, 5f, 6f })
                    {
                        float value = wear;
                        check(label + "wear " + value + " follows the native seizure threshold", () =>
                        {
                            f.Receive(value, 32, .7f, true); Require(f.Prepare(), "Wear update failed."); f.Fire("Water Pump");
                            Require(f.Reader.ActiveStateName == (value <= 5 ? "Pump seize" : "Wearing 2"), "Native seizure decision ignored host condition."); f.AssertSaved();
                        });
                    }
                    check(label + "native durability arithmetic cannot wear the saved pump", () =>
                    {
                        f.Readers[2].OnEnter(); f.Reader.FsmVariables.FindFsmFloat("WaterPSI").Value = 47; f.Fire("Wearing 2");
                        Require(Near(f.Reader.FsmVariables.FindFsmFloat("WearRateWaterpump").Value, .007f), "Native wear calculation ignored host durability.");
                        Require(!f.Action("Wearing 2", 4).Enabled, "Native saved-wear write was enabled."); f.AssertSaved();
                    });
                    check(label + "host publication carries actual pump durability and efficiency", () =>
                    {
                        Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false);
                        f.Part.FsmVariables.FindFsmFloat("Durability").Value = .73f; f.Part.FsmVariables.FindFsmFloat("Efficiency").Value = 1.93f;
                        try
                        {
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                            Require(state != null && state.Scalars.Length == 4 && Near(state.Scalars[2], .73f) && Near(state.Scalars[3], 1.93f), "Host omitted actual part values.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                            Require(Near(decoded.Scalars[2], .73f) && Near(decoded.Scalars[3], 1.93f), "Pump values were lost on the wire.");
                        }
                        finally { Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                    });
                }
                check(label + "accepted removal closes inputs before the old view moves", () =>
                {
                    f.Receive(90, 0, .7f, false, false); Require(f.Prepare(), "Removal failed."); f.ReadAll();
                    Require(!f.Installed && f.Wear == 0 && f.Part.transform.parent == f.Mount.transform, "Old fitted view retained removed pump inputs.");
                    f.Fire(cooling ? "Water Pump 2" : "Water Pump");
                    Require(cooling ? Near(f.Reader.FsmVariables.FindFsmFloat("Circulation").Value, .0001f) : f.Reader.ActiveStateName == "Calculate leak", "Native absent-pump gate failed."); f.AssertSaved();
                });
                check(label + "refit waits for normal native entry", () =>
                {
                    string state = f.Reader.ActiveStateName; f.Receive(90, 32, .7f, true); Require(f.Prepare(), "Refit failed.");
                    Require(f.Reader.ActiveStateName == state, "Host input forced a native replay."); f.ReadAll(); Require(f.Installed, "Refitted pump stayed absent.");
                });
                check(label + "changed scalar reader pauses its consumer and recovers", () =>
                {
                    var reader = f.Readers[f.Readers.Length - 1]; var original = Get(reader, "variableName"); Set(reader, "variableName", new FsmString { Value = "Other" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Malformed reader escaped containment."); f.AssertSaved(); }
                    finally { Set(reader, "variableName", original); f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired consumer stayed paused."); f.ReadAll(); Require(f.Installed, "Repaired reader borrowed the saved pump.");
                });
                if (cooling) check(label + "both consumers share accepted state with independent failure containment", () =>
                {
                    var oil = f.AddConsumer("Oil");
                    var installed = NativeBagPartChecks.State(oil, "Water Pump").Actions[1];
                    var wear = NativeBagPartChecks.State(oil, "Water Pump").Actions[3];
                    var durability = NativeBagPartChecks.State(oil, "Starting engine").Actions[4];
                    try
                    {
                        f.Receive(83, 32, .6f, true, efficiency: 2.2f); Require(f.Prepare(), "Two consumers could not bind the same host family.");
                        f.ReadAll(); installed.OnEnter(); wear.OnEnter(); durability.OnEnter();
                        Require(f.Installed && f.Wear == 83 && Near(f.Efficiency, 2.2f)
                            && oil.FsmVariables.FindFsmBool("Installed1").Value && oil.FsmVariables.FindFsmFloat("Wear").Value == 83
                            && Near(oil.FsmVariables.FindFsmFloat("DurabilityWaterPump").Value, .6f), "Consumers disagreed on the accepted pump.");
                        Require(f.Proxy != oil.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)Get(installed, "gameObject")), "Consumer proxies were accidentally shared.");
                        var original = Get(durability, "fsmName"); Set(durability, "fsmName", new FsmString { Value = "Changed" });
                        try
                        {
                            f.Receive(84, 32, .8f, true, efficiency: 2.3f); Require(!f.Prepare() && !oil.enabled && f.Reader.enabled, "One changed consumer disabled the other graph.");
                            f.ReadAll(); Require(f.Wear == 84 && Near(f.Efficiency, 2.3f), "Healthy consumer stopped accepting host values.");
                        }
                        finally { Set(durability, "fsmName", original); f.Prepare(); }
                        Require(oil.enabled, "Repaired companion stayed paused."); durability.OnEnter(); Require(Near(oil.FsmVariables.FindFsmFloat("DurabilityWaterPump").Value, .8f), "Repaired companion missed current host state.");
                        f.Receive(84, 0, .8f, false, false); Require(f.Prepare(), "Shared removal failed."); f.ReadAll(); installed.OnEnter();
                        Require(!f.Installed && !oil.FsmVariables.FindFsmBool("Installed1").Value, "Removal left one consumer installed."); f.AssertSaved();
                    }
                    finally { UnityEngine.Object.DestroyImmediate(oil.gameObject); f.Receive(90, 32, .7f, true); f.Prepare(); }
                });
                check(label + "disconnect and cleanup neutralize inputs then restore original caches", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare() == !cooling && (!cooling || !f.Reader.enabled), "Disconnected Cooling must pause until cleanup."); f.ReadAll(); Require(!f.Installed && f.Wear == 0, "Disconnected guest retained pump inputs.");
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Original reader wrapper was lost.");
                    f.ReadAll(); Require(f.Installed && f.Wear == 77 && (cooling ? f.Efficiency == 3 : f.Durability == 2), "Restored native cache retained a proxy."); f.AssertSaved();
                });
            }
        }

        internal static void RunFuelpump(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "FuelLine", "Wearing" })
            using (var f = new Fixture("VIN125", consumer))
            {
                bool fuel = consumer == "FuelLine"; var alternate = f.Alternative ?? throw new InvalidOperationException("Missing racing fixture.");
                string label = "fuel pump " + consumer + ": ";
                check(label + "warmed native readers initially use the guest saved pump", () =>
                {
                    f.ReadAll(); Require(fuel ? f.Installed && f.Wear == 77 && f.OutputRate == 400 : f.Durability == 2, "Saved pump baseline failed.");
                    foreach (var reader in f.Readers) Require(ReferenceEquals(Get(reader, "goLastFrame"), f.Mount.gameObject), "Native cache was not warmed."); f.AssertSaved();
                });
                check(label + "missing and pending stock pump supply neutral inputs", () =>
                {
                    Require(f.Prepare(), "Missing pump prevented safe binding."); f.ReadAll(); Require(!f.Installed && (fuel ? f.OutputRate == 0 : f.Durability == 0), "Absent host pump borrowed saved data.");
                    f.Receive(90, 16, 1.2f, false, efficiency: 145); Require(f.Prepare(), "Pending pump failed."); f.ReadAll(); Require(!f.Installed, "Unapplied pump supplied inputs.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied pump failed."); f.ReadAll();
                    Require(f.Installed && (fuel ? f.Wear == 90 && f.OutputRate == 145 : Near(f.Durability, 1.2f)), "Stock pump values missed native readers."); f.AssertSaved();
                });
                check(label + "proxy owns only audited fields and leaves saved write targets intact", () =>
                {
                    foreach (var reader in f.Readers) Require(f.Target(reader) == f.Proxy && ReferenceEquals(Get(reader, "goLastFrame"), f.Proxy), "Reader cache retained saved data.");
                    Require(!f.ProxyData.enabled && f.ProxyData.FsmVariables.FloatVariables.Length == (fuel ? 2 : 1)
                        && f.Shared.Value == f.Mount.gameObject, "Proxy changed native ownership or fields.");
                    if (!fuel) Require(!f.Action("Durability 2", 3).Enabled && f.Target(f.Action("Durability 2", 3)) == f.Mount.gameObject, "Saved pump wear writer was changed."); f.AssertSaved();
                });
                if (fuel)
                {
                    check(label + "stock pump passes the native installation gate", () =>
                    {
                        f.Fire("Fuel Pump"); Require(f.Reader.ActiveStateName == "Carburator", "Native installed gate ignored host pump.");
                    });
                    foreach (float wear in new[] { 4f, 5f, 6f })
                    {
                        float value = wear;
                        check(label + "wear " + value + " follows native fuel starvation threshold", () =>
                        {
                            f.Receive(value, 16, 1.2f, true, efficiency: 145); Require(f.Prepare(), "Wear update failed.");
                            f.Reader.FsmVariables.FindFsmFloat("Power").Value = 100; f.Fire("Fuel Usage");
                            Require(f.Reader.ActiveStateName == (value < 5 ? "Low fuel" : "Check speed"), "Native fuel starvation threshold diverged.");
                            Require(Near(f.Reader.FsmVariables.FindFsmFloat("FuelPumpEfficiency").Value, .006f), "Native worn-pump efficiency clamp diverged."); f.AssertSaved();
                        });
                    }
                    check(label + "stock output limits native fuel supply under load", () =>
                    {
                        f.Receive(90, 16, 1.2f, true, efficiency: 145); f.Prepare(); f.ReadAll();
                        f.Reader.FsmVariables.FindFsmFloat("Power").Value = 200; f.Fire("Fuel Usage");
                        Require(f.Reader.ActiveStateName == "Low fuel" && Near(f.Reader.FsmVariables.FindFsmFloat("FuelPumpEfficiency").Value, 90f / 8000), "Stock pump capacity did not constrain native fuel supply."); f.AssertSaved();
                    });
                }
                else check(label + "native wear arithmetic uses host durability without saved wear writes", () =>
                {
                    f.Reader.FsmVariables.FindFsmFloat("Wear2").Value = .25f; f.Fire("Durability 2");
                    Require(Near(f.Reader.FsmVariables.FindFsmFloat("Multiplier").Value, .3f), "Native wear arithmetic ignored stock durability."); f.AssertSaved();
                });
                check(label + "pending competing racing pump blocks old stock inputs", () =>
                {
                    var proxy = f.Proxy; f.ReceiveVariant(alternate, 95, .9f, 357, false); Require(f.Prepare(), "Conflicting variants broke preparation."); f.ReadAll();
                    Require(!f.Installed && f.Proxy == proxy && (fuel ? f.OutputRate == 0 : f.Durability == 0), "Two host occupants retained stock pump input."); f.AssertSaved();
                });
                check(label + "removing stock does not expose an unapplied racing pump", () =>
                {
                    f.Receive(90, 0, 1.2f, false, false, 145); Require(f.Prepare(), "Stock removal failed."); f.ReadAll(); Require(!f.Installed, "Unapplied alternative supplied input.");
                    if (fuel) { f.Fire("Fuel Pump"); Require(f.Reader.ActiveStateName == "Not Ok 4" && f.Starter.FsmVariables.FindFsmBool("ShutOff").Value, "Native missing-pump shutdown failed."); }
                    f.AssertSaved();
                });
                check(label + "applied racing pump replaces values without proxy rebuild or native replay", () =>
                {
                    var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                    f.ReceiveVariant(alternate, 95, .9f, 357, true); Require(f.Prepare(), "Racing pump failed to bind.");
                    Require(f.Proxy == proxy && f.Reader.ActiveStateName == active && (fuel ? f.OutputRate == 0 : f.Durability == 0), "Variant replacement replayed reads or rebuilt its proxy.");
                    f.ReadAll(); Require(f.Installed && (fuel ? f.Wear == 95 && f.OutputRate == 357 : Near(f.Durability, .9f)), "Racing values missed native readers.");
                    if (fuel)
                    {
                        f.Fire("Fuel Pump"); Require(f.Reader.ActiveStateName == "Carburator", "Normal retry missed racing pump.");
                        f.Reader.FsmVariables.FindFsmFloat("Power").Value = 200; f.Fire("Fuel Usage");
                        Require(f.Reader.ActiveStateName == "Check speed", "Racing pump kept the stock output limit.");
                    }
                    else { f.Reader.FsmVariables.FindFsmFloat("Wear2").Value = .25f; f.Fire("Durability 2"); Require(Near(f.Reader.FsmVariables.FindFsmFloat("Multiplier").Value, .225f), "Racing durability missed native math."); }
                    f.AssertSaved();
                });
                check(label + "host publishes actual fields for both pump variants", () =>
                {
                    foreach (bool racing in new[] { false, true })
                    {
                        object binding = racing ? alternate.Binding : f.Binding; var part = racing ? alternate.Part : f.Part;
                        uint id = racing ? alternate.PartId : f.PartId; float durability = racing ? .93f : 1.23f, output = racing ? 359 : 147;
                        Property(f.Session, "IsHost", true); Set(binding, "Replica", false);
                        part.FsmVariables.FindFsmFloat("Durability").Value = durability; part.FsmVariables.FindFsmFloat("OutputRate").Value = output;
                        try
                        {
                            var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", id);
                            Require(state != null && state.Scalars.Length == 4 && Near(state.Scalars[2], durability) && state.Scalars[3] == output, "Host publication omitted native pump fields.");
                            var decoded = (ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!));
                            Require(Near(decoded.Scalars[2], durability) && decoded.Scalars[3] == output, "Pump fields were lost on wire.");
                        }
                        finally { Property(f.Session, "IsHost", false); Set(binding, "Replica", true); }
                    }
                    f.AssertSaved();
                });
                check(label + "changed alternative factory mount pauses and repairs the consumer", () =>
                {
                    var reference = ((PlayMakerFSM)Get(alternate.Factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP"); reference.Value = f.Original.gameObject;
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Mismatched variant mount escaped validation."); f.AssertSaved(); }
                    finally { reference.Value = f.Mount.gameObject; f.Prepare(); }
                    Require(f.Reader.enabled, "Repaired variant mount remained paused."); f.ReadAll(); Require(f.Installed, "Repaired variant failed to supply input.");
                });
                check(label + "unavailable applied racing identity cannot retain engine input", () =>
                {
                    var id = alternate.Part.FsmVariables.FindFsmString("ID"); string original = id.Value; id.Value = "FUELPUMP099";
                    try { Require(f.Prepare(), "Unavailable replica failed safe preparation."); f.ReadAll(); Require(!f.Installed, "Changed replica identity supplied fuel input."); }
                    finally { id.Value = original; f.Prepare(); }
                    f.ReadAll(); Require(f.Installed, "Restored replica did not recover."); f.AssertSaved();
                });
                check(label + "moving the block preserves both variant mount bindings", () =>
                {
                    var original = f.Anchor.transform.parent; f.Anchor.transform.SetParent(f.Car.transform, false);
                    try { Require(f.Prepare(), "Block movement rejected live factory references."); f.ReadAll(); Require(f.Installed, "Moved pump lost accepted attachment."); }
                    finally { f.Anchor.transform.SetParent(original, false); }
                });
                if (fuel) check(label + "simultaneous fuel and wear consumers recover independently", () =>
                {
                    var wearing = f.AddConsumer("Wearing"); var read = NativeBagPartChecks.State(wearing, "State 4").Actions[1];
                    try
                    {
                        Require(f.Prepare(), "Both fuel consumers could not bind."); f.ReadAll(); read.OnEnter();
                        Require(f.OutputRate == 357 && Near(wearing.FsmVariables.FindFsmFloat("DurabilityFuelpump").Value, .9f), "Consumers did not agree on racing pump.");
                        var original = Get(read, "variableName"); Set(read, "variableName", new FsmString { Value = "Changed" });
                        try { Require(!f.Prepare() && !wearing.enabled && f.Reader.enabled, "One changed reader disabled both consumers."); f.ReadAll(); Require(f.Installed && f.OutputRate == 357, "Healthy fuel consumer lost accepted state."); }
                        finally { Set(read, "variableName", original); f.Prepare(); }
                        Require(wearing.enabled, "Repaired wear consumer stayed paused.");
                        f.ReceiveVariant(alternate, 95, .9f, 357, false, false); Require(f.Prepare(), "Shared removal failed."); f.ReadAll(); read.OnEnter();
                        Require(!f.Installed && wearing.FsmVariables.FindFsmFloat("DurabilityFuelpump").Value == 0, "Removed variant remained available to one consumer.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(wearing); f.ReceiveVariant(alternate, 95, .9f, 357, true); f.Prepare(); }
                });
                check(label + "racing removal and stock refit cannot leak the old variant", () =>
                {
                    f.ReceiveVariant(alternate, 95, .9f, 357, false, false); Require(f.Prepare(), "Racing removal failed."); f.ReadAll(); Require(!f.Installed, "Removed racing pump retained input.");
                    f.Receive(88, 16, 1.2f, true, efficiency: 145); Require(f.Prepare(), "Stock refit failed."); f.ReadAll();
                    Require(f.Installed && (fuel ? f.Wear == 88 && f.OutputRate == 145 : Near(f.Durability, 1.2f)), "Stock refit retained racing fields."); f.AssertSaved();
                });
                check(label + "disconnect and cleanup neutralize inputs then restore original caches", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Disconnect failed."); f.ReadAll(); Require(!f.Installed && (fuel ? f.OutputRate == 0 : f.Durability == 0), "Disconnected guest retained fuel pump input.");
                    Call(f.Sync, "RestoreGuestEngineInputs");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Original native owner wrapper lost.");
                    f.ReadAll(); Require(fuel ? f.Installed && f.Wear == 77 && f.OutputRate == 400 : f.Durability == 2, "Restored cache retained owned inputs."); f.AssertSaved();
                });
            }
        }
        internal static void RunOilpump(Action<string, Action> check)
        {
            foreach (string consumer in new[] { "Oil", "Wearing" })
            using (var f = new Fixture("VIN132", consumer))
            {
                bool oil = consumer == "Oil"; string label = "oil pump " + consumer + ": ";
                var auxiliary = f.Auxiliary(oil ? "VIN126" : "VIN125"); var other = auxiliary.Variants[0];
                var otherRead = f.Action(oil ? "Water Pump" : "State 4", oil ? 3 : 1);
                var otherOwner = Get(otherRead, "gameObject");
                check(label + "native readers start with saved part data", () =>
                {
                    f.ReadAll(); Require(oil ? f.Installed && f.Wear == 77 : f.Durability == 2, "Saved pump baseline failed.");
                    foreach (var read in f.Readers) Require(ReferenceEquals(Get(read, "goLastFrame"), f.Mount.gameObject), "Native cache was not warmed."); f.AssertSaved();
                });
                check(label + "cold and pending host state stay absent across both sources", () =>
                {
                    Require(f.Prepare(), "Multiple source binding failed."); f.ReadAll(); otherRead.OnEnter();
                    Require(!f.Installed && (oil ? f.Wear == 0 : f.Durability == 0), "Cold inputs borrowed guest data.");
                    f.Receive(90, 16, 1.1f, false); Require(f.Prepare(), "Pending preparation failed."); f.ReadAll(); Require(!f.Installed, "Pending oil pump supplied input.");
                    f.MarkApplied(); Require(f.Prepare(), "Applied preparation failed."); f.ReadAll();
                    Require(f.Installed && (oil ? f.Wear == 90 : Near(f.Durability, 1.1f)), "Applied oil pump missed native readers.");
                    f.AssertSaved();
                });
                check(label + "one graph keeps independent proxies and native shared scratch", () =>
                {
                    f.ReceiveVariant(other, 24, .7f, 145, true); f.AssertVariantReady(other); Require(f.Prepare(), "Additional part state failed.");
                    var otherProxy = f.Target(otherRead); Require(otherProxy != f.Proxy && otherProxy != auxiliary.Mount.gameObject, "Sources shared one proxy or retained saved data.");
                    f.ReadAll(); otherRead.OnEnter();
                    if (oil)
                    {
                        Require(f.Wear == 24, "Water pump did not own its normal shared scratch read: " + f.Wear);
                        Require(f.Prepare() && f.Wear == 24, "Projection overwrote another part's calculation scratch.");
                        f.ReadAll(); Require(f.Wear == 90, "Oil pump did not regain scratch at its own read entry.");
                    }
                    else Require(Near(f.Durability, 1.1f) && Near(f.Reader.FsmVariables.FindFsmFloat("DurabilityFuelpump").Value, .7f), "One source replaced the other durability: " + f.Durability + "/" + f.Reader.FsmVariables.FindFsmFloat("DurabilityFuelpump").Value);
                    Require(((IList)Get(f.Sync, "_guestEngineInputs")).Count == 10, "Shared graph did not retain all source bindings."); f.AssertSaved();
                });
                if (oil)
                {
                    foreach (float wear in new[] { 12f, 13f, 14f })
                    {
                        float value = wear;
                        check(label + "wear " + value + " follows the native oil circulation threshold", () =>
                        {
                            f.Receive(value, 16, 1.1f, true); Require(f.Prepare(), "Oil condition update failed."); f.Fire("Oil pump?");
                            Require(f.Reader.ActiveStateName == (value < 13 ? "Starving" : "Friction") && f.Wear == value, "Native oil starvation threshold diverged."); f.AssertSaved();
                        });
                    }
                }
                else check(label + "native oil-pump durability arithmetic preserves saved wear", () =>
                {
                    f.Reader.FsmVariables.FindFsmFloat("Wear2").Value = .25f; f.Fire("Durability 1");
                    Require(Near(f.Reader.FsmVariables.FindFsmFloat("Multiplier").Value, .275f), "Native oil-pump wear arithmetic ignored host durability.");
                    Require(!f.Action("Durability 1", 1).Enabled && f.Target(f.Action("Durability 1", 1)) == f.Mount.gameObject, "Saved wear writer was enabled or retargeted."); f.AssertSaved();
                });
                check(label + "host publication appends actual oil-pump durability", () =>
                {
                    Property(f.Session, "IsHost", true); Set(f.Binding, "Replica", false); f.Part.FsmVariables.FindFsmFloat("Durability").Value = 1.17f;
                    try
                    {
                        var state = (ReplacementPartState?)Call(f.Sync, "BuildReplacementPartState", f.PartId);
                        Require(state != null && state.Scalars.Length == 3 && Near(state.Scalars[2], 1.17f), "Host omitted actual oil-pump durability.");
                        Require(Near(((ReplacementPartState)PacketCodec.Decode(PacketCodec.Encode(state!))).Scalars[2], 1.17f), "Durability was lost on wire.");
                    }
                    finally { Property(f.Session, "IsHost", false); Set(f.Binding, "Replica", true); }
                    f.AssertSaved();
                });
                check(label + "removal closes only that part before its fitted view moves", () =>
                {
                    f.Receive(90, 0, 1.1f, false, false); Require(f.Prepare(), "Removal failed."); f.ReadAll(); Require(!f.Installed, "Removed oil pump remained installed.");
                    otherRead.OnEnter(); Require(oil ? f.Wear == 24 : Near(f.Reader.FsmVariables.FindFsmFloat("DurabilityFuelpump").Value, .7f), "Oil-pump removal cleared unrelated part input.");
                    if (oil) { f.Fire("Oil pump?"); Require(f.Reader.ActiveStateName == "No circulation", "Native absent oil-pump gate failed."); }
                    Require(f.Part.transform.parent == f.Mount.transform, "Removal fixture prematurely moved its old presentation."); f.AssertSaved();
                });
                check(label + "refitting updates the same proxy at the next native read", () =>
                {
                    var proxy = f.Proxy; string active = f.Reader.ActiveStateName;
                    f.Receive(91, 16, .8f, true); Require(f.Prepare(), "Refit failed.");
                    Require(f.Proxy == proxy && f.Reader.ActiveStateName == active, "Refit rebuilt or replayed the native graph.");
                    f.ReadAll(); Require(f.Installed && (oil ? f.Wear == 91 : Near(f.Durability, .8f)), "Refit did not supply new host data.");
                });
                check(label + "broken second source blocks native entry until both sources validate", () =>
                {
                    var read = f.Readers[f.Readers.Length - 1]; var original = Get(read, "variableName"); Set(read, "variableName", new FsmString { Value = "Wrong" });
                    try
                    {
                        Require(!f.Prepare() && !f.Reader.enabled, "A valid first source let the broken graph run.");
                        f.Fire(oil ? "Oil pump?" : "Durability 1"); Require(!f.Reader.enabled, "Native entry escaped the source readiness guard."); f.AssertSaved();
                    }
                    finally { Set(read, "variableName", original); f.Prepare(); }
                    Require(f.Reader.enabled, "Fully repaired shared graph remained paused."); f.ReadAll(); Require(f.Installed, "Repair restored a saved input instead of host state.");
                });
                check(label + "broken first source cannot leave a partially ready graph running", () =>
                {
                    var original = Get(otherRead, "variableName"); Set(otherRead, "variableName", new FsmString { Value = "Wrong" });
                    try { Require(!f.Prepare() && !f.Reader.enabled, "Second source bypassed an invalid first source."); f.AssertSaved(); }
                    finally { Set(otherRead, "variableName", original); f.Prepare(); }
                    Require(f.Reader.enabled, "First-source repair did not recover graph."); f.ReadAll(); Require(f.Installed, "Recovered oil pump stayed absent.");
                });
                check(label + "destroyed oil-pump proxy rebuilds without replacing the other source", () =>
                {
                    var original = f.Proxy; var untouched = f.Target(otherRead); UnityEngine.Object.DestroyImmediate(f.ProxyData);
                    Require(f.Prepare(), "Destroyed source proxy did not recover."); f.ReadAll();
                    Require(f.Proxy != original && f.Target(otherRead) == untouched && f.Installed, "Proxy recovery replaced the wrong source or kept a stale cache."); f.AssertSaved();
                });
                if (oil) check(label + "a shared-graph failure leaves the other consumer operational", () =>
                {
                    var wearing = f.AddConsumer("Wearing"); var durability = NativeBagPartChecks.State(wearing, "State 4").Actions[2];
                    try
                    {
                        Require(f.Prepare(), "Both oil-pump consumers failed to prepare."); durability.OnEnter(); Require(Near(wearing.FsmVariables.FindFsmFloat("DurabilityOilpump").Value, .8f), "Second consumer missed host pump.");
                        var read = f.Readers[1]; var original = Get(read, "variableName"); Set(read, "variableName", new FsmString { Value = "Wrong" });
                        try
                        {
                            f.Receive(92, 16, .6f, true); Require(!f.Prepare() && !f.Reader.enabled && wearing.enabled, "Failure escaped its native graph.");
                            durability.OnEnter(); Require(Near(wearing.FsmVariables.FindFsmFloat("DurabilityOilpump").Value, .6f), "Healthy consumer stopped receiving updates.");
                        }
                        finally { Set(read, "variableName", original); f.Prepare(); }
                        Require(f.Reader.enabled, "Repaired oil graph stayed paused.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(wearing); f.Prepare(); }
                });
                check(label + "disconnect neutralizes every source in the graph", () =>
                {
                    Property(f.Session, "State", SessionState.Idle); Require(f.Prepare(), "Disconnect failed."); f.ReadAll(); otherRead.OnEnter();
                    Require(!f.Installed && (oil ? f.Wear == 0 : f.Durability == 0 && f.Reader.FsmVariables.FindFsmFloat("DurabilityFuelpump").Value == 0), "Disconnected graph retained host inputs."); f.AssertSaved();
                });
                check(label + "partial cleanup retains protection until all source targets restore", () =>
                {
                    var read = f.Readers[0]; var owned = Get(read, "gameObject"); var proxy = f.Proxy;
                    Set(read, "gameObject", new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject, GameObject = new FsmGameObject { Value = proxy } });
                    try
                    {
                        Call(f.Sync, "RestoreGuestEngineInputs"); Require(!f.Reader.enabled && proxy != null, "Partial restoration discarded a still-referenced proxy.");
                        Require(ReferenceEquals(Get(otherRead, "gameObject"), otherOwner), "Independent source did not restore its original target.");
                        Require(!f.Prepare() && !f.Reader.enabled, "Partial cleanup resumed a mixed native graph."); f.AssertSaved();
                    }
                    finally { Set(read, "gameObject", owned); f.Prepare(); Call(f.Sync, "RestoreGuestEngineInputs"); }
                    Require(f.Reader.enabled, "Complete restoration left the native graph paused.");
                    for (int i = 0; i < f.Readers.Length; i++) Require(ReferenceEquals(Get(f.Readers[i], "gameObject"), f.OriginalOwners[i]), "Original oil-pump reader wrapper was lost.");
                    f.ReadAll(); Require(oil ? f.Installed && f.Wear == 77 : f.Durability == 2, "Restored reader retained host proxy cache."); f.AssertSaved();
                });
            }
        }
    }
}
