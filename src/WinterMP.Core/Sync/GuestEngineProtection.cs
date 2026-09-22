using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal static partial class GuestEngineProtection
    {
        private sealed class Write
        {
            internal FsmState State = null!;
            internal FsmStateAction Action = null!;
            internal int Index;
            internal GuestEngineWriteActionData? StarterRule;
            internal GuestEngineWriteActionData? GearboxUseRule;
            internal uint ObservedEntry;
        }

        private sealed class Binding
        {
            internal PlayMakerFSM Fsm = null!;
            internal FsmState[] States = null!;
            internal object Rule = null!;
            internal uint Entry;
            internal readonly List<Write> Writes = new List<Write>();
            internal Write? GearboxConditionRead;
            internal readonly List<Write> WheelHealthReads = new List<Write>();
            internal readonly List<Write> DrivetrainWearReads = new List<Write>();
            internal FsmSuppressor? Pause;
        }

        private sealed class PendingInitialization
        {
            internal Fsm Native = null!;
            internal object Rule = null!;
            internal FsmState[] States = null!;
        }

        private sealed class ActionMetadata
        {
            internal string? NativeName;
            internal bool NativeTypeChecked;
            internal readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        }

        // CLR type layout is fixed for this AppDomain. Cache metadata only;
        // action slots, field values and Unity identities must stay live.
        private static readonly Dictionary<Type, ActionMetadata> ActionTypes = new Dictionary<Type, ActionMetadata>();

        private static ActionMetadata Metadata(Type type)
        {
            if (ActionTypes.TryGetValue(type, out var metadata)) return metadata;
            metadata = new ActionMetadata();
            ActionTypes.Add(type, metadata);
            return metadata;
        }

        private static bool NativeActionType(Type type, string expected)
        {
            var metadata = Metadata(type);
            if (!metadata.NativeTypeChecked)
            {
                string name = type.Name;
                if (type.FullName == "HutongGames.PlayMaker.Actions." + name
                    && type.Assembly.GetName().Name == "Assembly-CSharp"
                    && type.GetMethod("OnExit")?.DeclaringType == typeof(FsmStateAction)) metadata.NativeName = name;
                metadata.NativeTypeChecked = true;
            }
            return metadata.NativeName != null && metadata.NativeName == expected;
        }

        private sealed class FsmIdentity : IEqualityComparer<PlayMakerFSM>
        {
            public bool Equals(PlayMakerFSM? left, PlayMakerFSM? right) => ReferenceEquals(left, right);
            public int GetHashCode(PlayMakerFSM fsm) => RuntimeHelpers.GetHashCode(fsm);
        }

        private static readonly Dictionary<PlayMakerFSM, Binding> Bindings = new Dictionary<PlayMakerFSM, Binding>(new FsmIdentity());
        private static readonly Dictionary<PlayMakerFSM, FsmSuppressor> Failures = new Dictionary<PlayMakerFSM, FsmSuppressor>(new FsmIdentity());
        private static readonly Dictionary<PlayMakerFSM, FsmState> BlockedEntries = new Dictionary<PlayMakerFSM, FsmState>(new FsmIdentity());
        private static readonly Dictionary<PlayMakerFSM, PendingInitialization> PendingInitializations = new Dictionary<PlayMakerFSM, PendingInitialization>(new FsmIdentity());
        private static float _nextScanAt, _nextErrorAt;
        private static bool _lastReady;
        internal static Action<PlayMakerFSM>? PrepareInputs;

        internal static bool Prepare(bool force = false) => PrepareCore(force, false);
        internal static bool PrepareForAdmission() => PrepareCore(true, true);
        // Power may awaken dormant graphs. Their retained pauses and entry guard
        // must be ready first; simulation still waits for validated native inputs.
        internal static bool PrepareForActivation(bool force = false) => PrepareCore(force, true);

        private static bool PrepareCore(bool force, bool allowPending)
        {
            if (!GuestSaveGuard.ProtectWorld) return true;
            // Ordinary reassertion needs only its boolean result. Collect binding
            // sets only where admission or post-scan restoration consumes them.
            var pending = allowPending ? new HashSet<PlayMakerFSM>(new FsmIdentity()) : null;
            bool ready = Reassert(null, pending, !force && !allowPending);
            if (!Initialize() || SyncCatalog.GuestEngineProtection == null)
            {
                NoteFailure("catalog", new InvalidOperationException(SyncCatalog.GuestEngineProtectionError ?? "Engine protection metadata is unavailable."));
                return _lastReady = false;
            }
            if (!force && Time.unscaledTime < _nextScanAt)
                return ready && (_lastReady || pending != null && OnlyPendingFailures(pending));
            _nextScanAt = Time.unscaledTime + 3f;

            var validated = new HashSet<Binding>();
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null || !IsProtectedFsm(fsm)) continue;
                try
                {
                    if (DeferInitialization(fsm)) { if (!allowPending) ready = false; continue; }
                    var binding = Bind(fsm);
                    if (ReferenceEquals(binding.Rule, FindRule(fsm))) validated.Add(binding);
                }
                catch (Exception error) { ready = false; Fail(fsm, error); }
            }
            var applied = new HashSet<Binding>();
            ready = Reassert(applied, pending, false) && ready;
            // Restoration is safe only after every selected write has been removed
            // from both future entries and any already-active action lists.
            foreach (var pair in new List<KeyValuePair<PlayMakerFSM, FsmSuppressor>>(Failures))
            {
                if (Destroyed(pair.Key)) { Failures.Remove(pair.Key); BlockedEntries.Remove(pair.Key); PendingInitializations.Remove(pair.Key); continue; }
                if (!Bindings.TryGetValue(pair.Key, out var binding)
                    || !validated.Contains(binding) || !applied.Contains(binding)) continue;
                try
                {
                    Failures.Remove(pair.Key);
                    if (binding.Pause == null)
                    {
                        pair.Value.Restore();
                        ResumeBlockedEntry(binding);
                    }
                    else BlockedEntries.Remove(pair.Key);
                }
                catch (Exception error) { ready = false; Fail(pair.Key, error); }
            }
            bool simulationReady = ready && Failures.Count == 0;
            _lastReady = simulationReady;
            return simulationReady || ready && pending != null && OnlyPendingFailures(pending);
        }

        private static bool OnlyPendingFailures(HashSet<PlayMakerFSM> pending)
        {
            foreach (var fsm in Failures.Keys) if (!pending.Contains(fsm)) return false;
            return true;
        }

        private static bool DeferInitialization(PlayMakerFSM fsm)
        {
            var rule = FindRule(fsm);
            if (PendingInitializations.TryGetValue(fsm, out var pending))
            {
                if (!PendingIdentityCurrent(fsm, pending))
                    throw new InvalidOperationException("Deferred engine graph changed identity or definitions.");
                if (fsm.Fsm.Initialized) { PendingInitializations.Remove(fsm); return false; }
                if (!Dormant(fsm)) throw new InvalidOperationException("Deferred engine graph activated without native initialization.");
            }
            else if (Bindings.ContainsKey(fsm) || !(rule is GuestEngineWriterData) || !Dormant(fsm)) return false;

            var writer = rule as GuestEngineWriterData
                ?? throw new InvalidOperationException("Deferred engine writer metadata changed.");
            foreach (var action in writer.Actions) FindState(fsm, action.State);
            foreach (var action in writer.PoseActions) FindState(fsm, action.State);
            foreach (var action in writer.EventActions) FindState(fsm, action.State);
            foreach (var read in DrivetrainReaders(writer)) FindState(fsm, read.State);
            foreach (var read in writer.WheelHealthReads) FindState(fsm, read.State);
            if (writer.GearboxConditionRead != null) FindState(fsm, writer.GearboxConditionRead.State);
            if (pending == null)
                PendingInitializations.Add(fsm, new PendingInitialization {
                    Native = fsm.Fsm, Rule = writer, States = (FsmState[])fsm.Fsm.States.Clone() });

            // Awake belongs to native activation. Keep the component paused until
            // its actions can be validated; the entry guard also covers direct starts.
            Fail(fsm, new GuestEngineInputsPendingException("Waiting for native engine initialization."));
            return true;
        }

        private static bool Dormant(PlayMakerFSM fsm)
        {
            if (fsm.gameObject.activeInHierarchy || fsm.Fsm.Initialized || fsm.Fsm.Started || fsm.Fsm.States.Length == 0) return false;
            foreach (var state in fsm.Fsm.States)
                if (state == null || state.IsInitialized || state.ActionsLoaded || state.ActiveActions.Count != 0) return false;
            return true;
        }

        private static bool PendingIdentityCurrent(PlayMakerFSM fsm, PendingInitialization pending)
        {
            if (!ReferenceEquals(pending.Native, fsm.Fsm) || !ReferenceEquals(pending.Rule, FindRule(fsm))
                || pending.States.Length != fsm.Fsm.States.Length) return false;
            for (int i = 0; i < pending.States.Length; i++)
                if (!ReferenceEquals(pending.States[i], fsm.Fsm.States[i])) return false;
            return true;
        }

        internal static bool IsProtectedFsm(PlayMakerFSM fsm)
        {
            if (Bindings.ContainsKey(fsm) || Failures.ContainsKey(fsm)) return true;
            return FindRule(fsm) != null;
        }

        internal static bool GuardStateEntry(PlayMakerFSM fsm, FsmState state)
        {
            if (!GuestSaveGuard.ProtectWorld || !IsProtectedFsm(fsm)) return true;
            try
            {
                var binding = Bind(fsm);
                Disable(binding);
                FinishActive(binding);
                string? waiting = PrepareBoundInputs(binding);
                if (waiting != null) WaitForInputs(fsm, waiting);
                else if (binding.Pause == null && !Failures.ContainsKey(fsm))
                {
                    binding.Entry = binding.Entry == uint.MaxValue ? 1 : binding.Entry + 1;
                    ObserveWheelPuncture(binding, state);
                    BlockedEntries.Remove(fsm);
                    return true;
                }
            }
            catch (Exception error) { Fail(fsm, error); }
            BlockedEntries[fsm] = state;
            return false;
        }

        private static void ResumeBlockedEntry(Binding binding)
        {
            var fsm = binding.Fsm;
            if (!BlockedEntries.TryGetValue(fsm, out var state)) return;
            if (!ReferenceEquals(fsm.Fsm.ActiveState, state)) { BlockedEntries.Remove(fsm); return; }
            if (!fsm.enabled || !fsm.gameObject.activeInHierarchy) return;
            bool current = false;
            foreach (var candidate in binding.States)
                if (ReferenceEquals(candidate, state)) { current = true; break; }
            if (!current) throw new InvalidOperationException("The blocked native state has been replaced.");

            // EnterState already marked this state entered before our prefix stopped
            // OnEnter. Complete that entry without replaying stale OnExit actions.
            BlockedEntries.Remove(fsm);
            FsmExecutionStack.PushFsm(fsm.Fsm);
            try { state.OnEnter(); fsm.Fsm.UpdateStateChanges(); }
            finally { FsmExecutionStack.PopFsm(); }
        }

        private static object? FindRule(PlayMakerFSM fsm)
        {
            var profile = SyncCatalog.GuestEngineProtection;
            if (profile == null) return null;
            string? path = null, leaf = null;
            string name = fsm.FsmName;
            foreach (var rule in profile.Writers)
                if (rule.Fsm == name && ScenePath.MayMatchLeafName(rule.Path, leaf ?? (leaf = fsm.gameObject.name))
                    && rule.Path == (path ?? (path = ScenePath.Of(fsm.transform)))) return rule;
            foreach (var rule in profile.PausedFsms)
                if (rule.Fsm == name && (ScenePath.MayMatchLeafName(rule.Path, leaf ?? (leaf = fsm.gameObject.name))
                    && rule.Path == (path ?? (path = ScenePath.Of(fsm.transform)))
                    || rule.RootPrefix.Length != 0 && (leaf ?? (leaf = fsm.gameObject.name)) == rule.RelativePath
                        && MatchesMovingMount(fsm.transform, rule.RootPrefix, rule.RelativePath))) return rule;
            return null;
        }

        internal static bool MatchesMovingMount(Transform mount, string rootPrefix, string relativePath)
        {
            if (mount == null || mount.name != relativePath || mount.parent == null) return false;
            var root = mount.parent;
            // Native scene names protect pre-load assemblies; saved IDs also
            // recognize dynamically created blocks after their naming changes.
            if (NativeEnginePart(root.name, rootPrefix)) return true;
            foreach (var data in root.GetComponents<PlayMakerFSM>())
                if (data.FsmName == "Data" && NativeEnginePart(data.FsmVariables.FindFsmString("ID")?.Value, rootPrefix)) return true;
            return false;
        }

        internal static bool NativeEnginePart(string? id, string prefix) => id != null
            && (id == prefix + "0" || FactoryItemIdentity.IsNativeId(id, prefix));

        private static Binding Bind(PlayMakerFSM fsm)
        {
            var rule = FindRule(fsm);
            RememberExternalFloatTarget(fsm, rule);
            if (Bindings.TryGetValue(fsm, out var existing) && Current(existing)
                && (rule == null || ReferenceEquals(existing.Rule, rule)))
            {
                if (existing.WheelHealthReads.Count != 0)
                {
                    if (!ReferenceEquals(existing.Rule, rule))
                        throw new InvalidOperationException("Previously guarded wheel reader changed identity.");
                    foreach (var read in existing.WheelHealthReads) ValidateWheelHealthSignature(fsm, read.Action);
                }
                if (existing.GearboxConditionRead != null)
                    ValidateGearboxConditionSignature(fsm, existing.GearboxConditionRead.Action);
                ValidateDrivetrainProtection(existing);
                return existing;
            }
            if (rule == null) throw new InvalidOperationException("Previously guarded engine graph changed identity.");
            var binding = new Binding { Fsm = fsm, Rule = rule, States = (FsmState[])fsm.Fsm.States.Clone() };
            if (rule is GuestEngineWriterData writer)
            {
                foreach (var action in writer.Actions) binding.Writes.Add(ValidateWrite(fsm, action));
                foreach (var pose in writer.PoseActions) binding.Writes.Add(ValidatePose(fsm, pose));
                foreach (var action in writer.EventActions) binding.Writes.Add(ValidateProtectedEvent(fsm, action));
                foreach (var read in DrivetrainReaders(writer)) binding.DrivetrainWearReads.Add(ValidateDrivetrainReader(fsm, read));
                foreach (var read in writer.WheelHealthReads) binding.WheelHealthReads.Add(ValidateWheelHealthReader(fsm, read));
                if (writer.GearboxConditionRead != null)
                    binding.GearboxConditionRead = ValidateGearboxConditionReader(fsm, writer.GearboxConditionRead);
            }
            else if (rule is GuestEnginePausedFsmData paused)
            {
                foreach (string state in paused.RequiredStates) FindState(fsm, state);
                binding.Pause = existing?.Pause ?? new FsmSuppressor();
            }
            Bindings[fsm] = binding;
            SyncEventLog.Record("guest-engine-protected", ScenePath.Of(fsm.transform) + "::" + fsm.FsmName);
            return binding;
        }

        private static bool Current(Binding binding)
        {
            if (binding.Fsm == null || binding.States.Length != binding.Fsm.Fsm.States.Length) return false;
            for (int i = 0; i < binding.States.Length; i++)
                if (!ReferenceEquals(binding.States[i], binding.Fsm.Fsm.States[i])) return false;
            foreach (var write in binding.Writes)
                if (!FsmHook.IsNativeAction(write.State, write.Index, write.Action)) return false;
            var gearboxRead = binding.GearboxConditionRead;
            if (gearboxRead != null && (!FsmHook.IsNativeAction(gearboxRead.State, gearboxRead.Index, gearboxRead.Action)
                || !gearboxRead.Action.Enabled)) return false;
            foreach (var read in binding.WheelHealthReads)
                if (!FsmHook.IsNativeAction(read.State, read.Index, read.Action) || !read.Action.Enabled) return false;
            foreach (var read in binding.DrivetrainWearReads)
                if (!FsmHook.IsNativeAction(read.State, read.Index, read.Action) || !read.Action.Enabled) return false;
            return true;
        }

        private static Write ValidateWrite(PlayMakerFSM fsm, GuestEngineWriteActionData rule)
        {
            var write = FindAction(fsm, rule.State, rule.Index, rule.ActionType);
            var action = write.Action;
            if (!NamedTarget(Field<FsmOwnerDefault>(action, "gameObject"), rule.TargetVariable)
                || fsm.FsmVariables.FindFsmGameObject(rule.TargetVariable) == null
                || !Literal(Field<FsmString>(action, "fsmName"), rule.TargetFsm)
                || !Literal(Field<FsmString>(action, "variableName"), rule.TargetScalar))
                throw new InvalidOperationException("Changed engine scalar destination at " + rule.State + "#" + rule.Index + ".");
            if (rule.StarterDraw != 0)
            {
                ValidateStarterAmount(fsm, action, rule.StarterDraw);
                write.StarterRule = rule;
            }
            if (rule.StarterWear)
            {
                ValidateStarterWearAmount(fsm, action);
                write.StarterRule = rule;
            }
            if (rule.GearboxOilUse) { ValidateOilUseAmount(fsm, action); write.GearboxUseRule = rule; }
            if (rule.GearboxWear) { ValidateGearboxWearAmount(fsm, action); write.GearboxUseRule = rule; }
            return write;
        }

        private static Write ValidatePose(PlayMakerFSM fsm, GuestEnginePoseActionData rule)
        {
            var write = FindAction(fsm, rule.State, rule.Index, "SetRotation");
            var action = write.Action;
            var z = Field<FsmFloat>(action, "zAngle");
            if (!NamedTarget(Field<FsmOwnerDefault>(action, "gameObject"), rule.TargetVariable)
                || fsm.FsmVariables.FindFsmGameObject(rule.TargetVariable) == null
                || z == null || !z.UseVariable || z.Name != rule.AngleVariable
                || fsm.FsmVariables.FindFsmFloat(rule.AngleVariable) == null
                || Field<Space>(action, "space") != Space.Self
                || !None(Field<FsmFloat>(action, "xAngle")) || !None(Field<FsmFloat>(action, "yAngle"))
                || Field<bool>(action, "everyFrame") || Field<bool>(action, "lateUpdate"))
                throw new InvalidOperationException("Changed distributor pose destination at " + rule.State + "#" + rule.Index + ".");
            return write;
        }

        private static Write FindAction(PlayMakerFSM fsm, string stateName, int index, string typeName)
        {
            var state = FindState(fsm, stateName);
            var action = FsmHook.NativeAction(state, index)
                ?? throw new InvalidOperationException("Unavailable engine action " + stateName + "#" + index + ".");
            if (!NativeActionType(action.GetType(), typeName))
                throw new InvalidOperationException("Changed native engine action " + stateName + "#" + index + ".");
            return new Write { State = state, Index = index, Action = action };
        }

        private static FsmState FindState(PlayMakerFSM fsm, string name)
        {
            FsmState? found = null;
            foreach (var state in fsm.Fsm.States)
                if (state.Name == name)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous engine state " + name + ".");
                    found = state;
                }
            return found ?? throw new InvalidOperationException("Missing engine state " + name + ".");
        }

        private static T Field<T>(FsmStateAction action, string name)
        {
            var type = action.GetType(); var fields = Metadata(type).Fields;
            if (!fields.TryGetValue(name, out var field))
            {
                field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) throw new InvalidOperationException("Changed native engine field " + name + ".");
                fields.Add(name, field);
            }
            if (field.FieldType != typeof(T)) throw new InvalidOperationException("Changed native engine field " + name + ".");
            return (T)field.GetValue(action);
        }

        private static bool NamedTarget(FsmOwnerDefault target, string name) => target != null
            && target.OwnerOption == OwnerDefaultOption.SpecifyGameObject && target.GameObject != null
            && target.GameObject.UseVariable && target.GameObject.Name == name;
        private static bool Literal(FsmString value, string expected) => value != null && !value.UseVariable && value.Value == expected;
        private static bool None(FsmFloat value) => value != null && value.UseVariable && value.Name.Length == 0;

        private static bool Reassert(HashSet<Binding>? applied, HashSet<PlayMakerFSM>? pending, bool deferInputs)
        {
            RetireExternalFloatTargets();
            applied?.Clear(); pending?.Clear();
            bool ready = true;
            foreach (var pair in new List<KeyValuePair<PlayMakerFSM, FsmSuppressor>>(Failures))
            {
                if (Destroyed(pair.Key)) { Failures.Remove(pair.Key); BlockedEntries.Remove(pair.Key); PendingInitializations.Remove(pair.Key); continue; }
                try
                {
                    if (!pair.Value.Active && !pair.Value.Suppress(pair.Key)) throw new InvalidOperationException("Cannot retain failed engine protection.");
                    pair.Key.Fsm.RestartOnEnable = false;
                    pair.Key.enabled = false;
                    if (pending != null && _entryHookReady && pair.Value.Active
                        && PendingInitializations.TryGetValue(pair.Key, out var initialization)
                        && PendingIdentityCurrent(pair.Key, initialization) && Dormant(pair.Key))
                        pending.Add(pair.Key);
                }
                catch (Exception error) { ready = false; NoteFailure("failed graph reassertion", error); }
            }
            var live = new List<Binding>();
            foreach (var pair in new List<KeyValuePair<PlayMakerFSM, Binding>>(Bindings))
            {
                if (Destroyed(pair.Key)) { Bindings.Remove(pair.Key); BlockedEntries.Remove(pair.Key); continue; }
                try { Disable(pair.Value); live.Add(pair.Value); }
                catch (Exception error) { ready = false; Fail(pair.Key, error); }
            }
            foreach (var binding in live)
                try
                {
                    FinishActive(binding);
                    string? waiting = PrepareBoundInputs(binding, deferInputs && _entryHookReady && !Failures.ContainsKey(binding.Fsm));
                    if (waiting != null)
                    {
                        WaitForInputs(binding.Fsm, waiting);
                        if (pending != null && ContainedInputWait(binding)) pending.Add(binding.Fsm);
                        else ready = false;
                        continue;
                    }
                    applied?.Add(binding);
                    // A still-disabled entry cannot resume. Preserve stale-entry
                    // cleanup, and validate identity as soon as activation permits it.
                    if (BlockedEntries.TryGetValue(binding.Fsm, out var blocked) && !Failures.ContainsKey(binding.Fsm)
                        && (!ReferenceEquals(binding.Fsm.Fsm.ActiveState, blocked)
                            || binding.Fsm.enabled && binding.Fsm.gameObject.activeInHierarchy)
                        && Current(binding) && ReferenceEquals(binding.Rule, FindRule(binding.Fsm)))
                        ResumeBlockedEntry(binding);
                }
                catch (Exception error)
                {
                    Fail(binding.Fsm, error);
                    // Admission and activation can proceed while this graph stays
                    // durably paused for unavailable host inputs.
                    if (pending != null && error is GuestEngineInputsPendingException && ContainedInputWait(binding))
                        pending.Add(binding.Fsm);
                    else ready = false;
                }
            return ready;
        }

        private static bool ContainedInputWait(Binding binding) => Current(binding)
            && ReferenceEquals(binding.Rule, FindRule(binding.Fsm))
            && Failures.TryGetValue(binding.Fsm, out var pause) && pause.Active
            && !binding.Fsm.enabled && !binding.Fsm.Fsm.RestartOnEnable;

        private static void Disable(Binding binding)
        {
            if (binding.Pause != null)
            {
                if (!binding.Pause.Active && !binding.Pause.Suppress(binding.Fsm)) throw new InvalidOperationException("Cannot pause native engine part falling.");
                binding.Fsm.Fsm.RestartOnEnable = false;
                binding.Fsm.enabled = false;
            }
            foreach (var write in binding.Writes)
            {
                if (write.StarterRule != null) StarterObservers[write.Action] = write;
                if (write.GearboxUseRule != null) GearboxUseObservers[write.Action] = write;
                write.Action.Enabled = write.StarterRule != null
                    && WorldSyncManager.Instance?.CanObserveStarterDraw(binding.Fsm) == true
                    || write.GearboxUseRule != null && (write.GearboxUseRule.GearboxWear ? WorldSyncManager.Instance?.CanObserveGearboxWear(binding.Fsm) == true
                        : WorldSyncManager.Instance?.CanObserveGearboxOil(binding.Fsm) == true);
            }
        }

        private static void FinishActive(Binding binding)
        {
            // Paused saved-part graphs never resume in a protected world. Drain
            // work already queued before admission as well as stopping new ticks.
            if (binding.Pause != null)
                foreach (var state in binding.States)
                    if (state.ActiveActions.Count != 0)
                        foreach (var action in new List<FsmStateAction>(state.ActiveActions)) action.Finish();
            // PlayMaker 1.7 consults Enabled on entry, but not during OnUpdate.
            // Finish removes only this action; the next normal update advances the FSM.
            foreach (var write in binding.Writes)
                if (!write.Action.Enabled && write.State.ActiveActions.Contains(write.Action)) write.Action.Finish();
        }

        internal static void Fail(PlayMakerFSM fsm, Exception error)
        {
            if (error is GuestEngineInputsPendingException) { WaitForInputs(fsm, error.Message); return; }
            PauseFailedFsm(fsm);
            try { NoteFailure(ScenePath.Of(fsm.transform) + "::" + fsm.FsmName, error); }
            catch { NoteFailure("native graph", error); }
        }

        private static void WaitForInputs(PlayMakerFSM fsm, string reason)
        {
            if (PauseFailedFsm(fsm)) SyncEventLog.Record("guest-engine-input-waiting", fsm.FsmName + ": " + reason);
        }

        private static bool PauseFailedFsm(PlayMakerFSM fsm)
        {
            _lastReady = false;
            bool first = !Failures.ContainsKey(fsm);
            try
            {
                if (!Failures.TryGetValue(fsm, out var pause)) { pause = new FsmSuppressor(); Failures[fsm] = pause; }
                if (!pause.Active) pause.Suppress(fsm);
                fsm.Fsm.RestartOnEnable = false;
                fsm.enabled = false;
            }
            catch (Exception failure) { NoteFailure("pause failed", failure); }
            return first;
        }

        // Unity's destroyed component still supplies the dictionary's managed key.
        private static bool Destroyed(PlayMakerFSM fsm) => fsm == null;

        internal static void NoteFailure(string context, Exception error)
        {
            try
            {
                if (Time.unscaledTime < _nextErrorAt) return;
                _nextErrorAt = Time.unscaledTime + 10f;
                WinterMPPlugin.Log.LogError("Guest engine protection: preserving original parts; " + context + ": " + error.Message);
                SyncEventLog.Record("guest-engine-protection-failed", context + ": " + error.Message);
            }
            catch { /* Diagnostics cannot reopen a native write boundary. */ }
        }
    }
}
