using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed class GuestEngineInputsPendingException : InvalidOperationException
    {
        internal GuestEngineInputsPendingException(string message) : base(message) { }
    }

    internal sealed partial class ItemWorldSync
    {
        private sealed class GuestEngineInputBinding
        {
            internal GuestEngineInputData Rule = null!;
            internal PlayMakerFSM Fsm = null!;
            internal FsmState[] States = null!;
            internal FsmGameObject Source = null!;
            internal GameObject Proxy = null!;
            internal PlayMakerFSM Data = null!;
            internal bool Rebind = true, InputsReady;
            internal readonly List<GuestEngineInputRead> Reads = new List<GuestEngineInputRead>();
            internal readonly HashSet<string> VariableNames = new HashSet<string>();
        }

        private sealed class GuestEngineInputRead
        {
            internal GuestEngineInputReaderData Rule = null!;
            internal FsmState State = null!;
            internal FsmStateAction Action = null!;
            internal FieldInfo TargetField = null!;
            internal FieldInfo FsmNameField = null!, VariableNameField = null!, EveryFrameField = null!, OutputField = null!;
            internal FsmOwnerDefault Original = null!;
            internal FsmOwnerDefault Target = null!;
            internal NamedVariable Output = null!;
        }

        // Managed identity survives Unity destroying a consumer. The list is bounded
        // by the catalog, and also retains old wrappers until they can be restored.
        private readonly List<GuestEngineInputBinding> _guestEngineInputs = new List<GuestEngineInputBinding>();
        private Action<PlayMakerFSM>? _prepareGuestEngineInputs;
        private GuestEngineInputsData? _guestEngineInputScope;
        private bool _guestEngineInputsRestoring;
        private bool _guestEngineInputOriginalsReady = true;

        internal bool PrepareGuestEngineInputs(bool force = false, bool admission = false)
        {
            var session = SessionManager.Instance;
            if (GuestSaveGuard.ProtectWorld && (admission || (session != null && !session.IsHost
                && session.State == SessionState.Connected)))
            {
                foreach (var valve in new List<GuestValveBinding>(_guestValveInputs))
                    if (valve.Fsm == null) RestoreGuestValveInputs(valve);
                foreach (var old in new List<GuestEngineInputBinding>(_guestEngineInputs))
                    if (old.Fsm == null)
                    {
                        if (old.Proxy != null) UnityEngine.Object.Destroy(old.Proxy);
                        _guestEngineInputs.Remove(old);
                    }
                if (_prepareGuestEngineInputs == null) _prepareGuestEngineInputs = PrepareGuestEngineInputFsm;
                if (_guestEngineInputScope == null) _guestEngineInputScope = SyncCatalog.GuestEngineInputs;
                GuestEngineProtection.PrepareInputs = _prepareGuestEngineInputs;
                if (_guestEngineInputs.Count == 0 && _guestValveInputs.Count == 0 && _guestEngineInputOriginalsReady) _guestEngineInputsRestoring = false;
                RefreshReplacementFactories();
            }
            return admission ? GuestEngineProtection.PrepareForAdmission() : GuestEngineProtection.Prepare(force);
        }

        private void PrepareGuestEngineInputFsm(PlayMakerFSM fsm)
        {
            if (!GuestSaveGuard.ProtectWorld) return;
            List<GuestEngineInputBinding>? bindings = null;
            foreach (var candidate in _guestEngineInputs)
                if (ReferenceEquals(candidate.Fsm, fsm))
                {
                    if (bindings == null) bindings = new List<GuestEngineInputBinding>();
                    bindings.Add(candidate);
                }
            List<GuestEngineInputData>? rules = null;
            var profile = _guestEngineInputScope ?? SyncCatalog.GuestEngineInputs;
            string name = fsm.FsmName;
            string? path = null;
            if (profile != null)
                foreach (var entry in profile.Entries)
                    if (entry.Fsm == name && entry.ReaderPath == (path ?? (path = ScenePath.Of(fsm.transform))))
                    {
                        if (rules == null) rules = new List<GuestEngineInputData>();
                        rules.Add(entry);
                    }
            if (rules == null && bindings == null) return;
            if (!_guestEngineInputOriginalsReady)
                throw new InvalidOperationException("Saved guest parts could not be restored; retaining engine input protection until restart.");
            if (_guestEngineInputsRestoring)
            {
                foreach (var valve in new List<GuestValveBinding>(_guestValveInputs)) if (ReferenceEquals(valve.Fsm, fsm)) RestoreGuestValveInputs(valve);
                if (bindings != null) foreach (var binding in bindings)
                {
                    RestoreGuestEngineInputReads(binding);
                    _guestEngineInputs.Remove(binding);
                }
                return;
            }
            if (!ReferenceEquals(profile, SyncCatalog.GuestEngineInputs))
                throw new InvalidOperationException("Guest engine input profile changed.");
            if (bindings != null) foreach (var binding in bindings)
                if (rules == null || !rules.Contains(binding.Rule))
                    throw new InvalidOperationException("Guest engine input consumer identity changed.");
            // The entry guard resumes the native graph only after every source is
            // ready. Shared native scratch remains owned by its individual reads.
            GuestEngineInputsPendingException? pending = null;
            if (rules != null) foreach (var rule in rules)
            {
                GuestEngineInputBinding? binding = null;
                if (bindings != null) foreach (var candidate in bindings)
                    if (ReferenceEquals(candidate.Rule, rule)) { binding = candidate; break; }
                try { PrepareGuestEngineInputSource(fsm, rule, binding, path); }
                catch (GuestEngineInputsPendingException error) { pending = error; }
                // Reuse selection's path only until the first source refresh.
                // Later sources must observe any intervening native changes.
                finally { path = null; }
            }
            if (profile != null) PrepareGuestValveInputs(fsm, profile.Valves);
            if (pending != null) throw pending;
        }

        private void PrepareGuestEngineInputSource(PlayMakerFSM fsm, GuestEngineInputData rule, GuestEngineInputBinding? binding, string? readerPath)
        {
            if (binding != null) binding.InputsReady = false;
            if (binding != null && (binding.Rebind || !GuestEngineInputActionsCurrent(binding) || binding.Proxy == null || binding.Data == null))
            {
                RestoreGuestEngineInputReads(binding);
                _guestEngineInputs.Remove(binding);
                binding = null;
            }
            if (binding == null)
            {
                binding = BindGuestEngineInputs(fsm, rule);
                // Binding creates a PlayMaker component and runs native Awake.
                readerPath = null;
            }
            try
            {
                ValidateGuestEngineInputs(binding, readerPath);
                UpdateGuestEngineInputValues(binding);
                binding.InputsReady = true;
            }
            catch (GuestEngineInputsPendingException) { throw; }
            catch { binding.Rebind = true; throw; }
        }

        private GuestEngineInputBinding BindGuestEngineInputs(PlayMakerFSM fsm, GuestEngineInputData rule)
        {
            FsmGameObject? source;
            if (rule.DirectTarget)
            {
                var first = rule.Readers[0]; var state = GuestEngineInputState(fsm, first.State);
                var action = FsmHook.NativeAction(state, first.ActionIndex)
                    ?? throw new InvalidOperationException("Unavailable direct engine input action.");
                source = PackageField<FsmOwnerDefault>(action, "gameObject")?.GameObject;
            }
            else source = fsm.FsmVariables.FindFsmGameObject(rule.TargetVariable);
            if (source == null) throw new InvalidOperationException("Missing native engine input reference.");
            var binding = new GuestEngineInputBinding { Rule = rule, Fsm = fsm, Source = source,
                States = (FsmState[])fsm.Fsm.States.Clone() };
            foreach (var reader in rule.Readers)
            {
                var state = GuestEngineInputState(fsm, reader.State);
                var action = FsmHook.NativeAction(state, reader.ActionIndex)
                    ?? throw new InvalidOperationException("Unavailable native engine input action.");
                var type = action.GetType();
                if (type.FullName != "HutongGames.PlayMaker.Actions." + reader.ActionType
                    || type.Assembly.GetName().Name != "Assembly-CSharp")
                    throw new InvalidOperationException("Native engine input action type changed.");
                var field = GuestEngineInputField(type, "gameObject", typeof(FsmOwnerDefault));
                var original = field.GetValue(action) as FsmOwnerDefault;
                if (original == null) throw new InvalidOperationException("Missing native engine input owner.");
                ValidateGuestEngineInputOwner(binding, original);
                var output = reader.ActionType == "GetFsmBool"
                    ? (NamedVariable?)fsm.FsmVariables.FindFsmBool(reader.Output)
                    : reader.ActionType == "GetFsmString" ? (NamedVariable?)fsm.FsmVariables.FindFsmString(reader.Output)
                    : reader.ActionType == "GetFsmInt" ? (NamedVariable?)fsm.FsmVariables.FindFsmInt(reader.Output)
                    : fsm.FsmVariables.FindFsmFloat(reader.Output);
                if (output == null) throw new InvalidOperationException("Missing native engine calculation output.");
                var read = new GuestEngineInputRead { Rule = reader, State = state, Action = action,
                    TargetField = field, Original = original, Output = output,
                    FsmNameField = GuestEngineInputField(type, "fsmName", typeof(FsmString)),
                    VariableNameField = GuestEngineInputField(type, "variableName", typeof(FsmString)),
                    EveryFrameField = GuestEngineInputField(type, "everyFrame", typeof(bool)),
                    OutputField = GuestEngineInputField(type, "storeValue", GuestEngineInputOutputType(reader.ActionType)) };
                ValidateGuestEngineInputRead(binding, read, original);
                binding.Reads.Add(read);
            }
            ValidateGuestEngineInputMount(binding);
            var proxy = new GameObject("WinterMP engine inputs " + rule.FamilyPrefix);
            binding.Proxy = proxy;
            _guestEngineInputs.Add(binding);
            try
            {
                // Awake must run before building the empty FSM; native Awake loads
                // serialized action data. This object has no saved-part identity.
                var data = proxy.AddComponent<PlayMakerFSM>();
                data.enabled = false;
                data.FsmName = rule.InputFsm;
                data.Fsm.States = new FsmState[0];
                var booleans = new List<FsmBool> { new FsmBool { Name = "Installed", Value = false } };
                var scalars = new List<FsmFloat>();
                var strings = new List<FsmString>();
                var integers = new List<FsmInt>();
                var names = new HashSet<string> { "Installed" };
                foreach (var read in rule.Readers)
                {
                    if (!names.Add(read.Variable)) continue;
                    if (read.ActionType == "GetFsmBool") booleans.Add(new FsmBool { Name = read.Variable, Value = read.Variable == "Damaged" });
                    if (read.ActionType == "GetFsmInt") integers.Add(new FsmInt { Name = read.Variable });
                    if (read.ActionType == "GetFsmFloat") scalars.Add(new FsmFloat { Name = read.Variable });
                    if (read.ActionType == "GetFsmString") strings.Add(new FsmString { Name = read.Variable, Value = "00000000" });
                }
                data.FsmVariables.BoolVariables = booleans.ToArray();
                data.FsmVariables.FloatVariables = scalars.ToArray();
                data.FsmVariables.StringVariables = strings.ToArray();
                data.FsmVariables.IntVariables = integers.ToArray();
                binding.Data = data;
                foreach (var read in binding.Reads)
                {
                    read.Target = new FsmOwnerDefault { OwnerOption = OwnerDefaultOption.SpecifyGameObject,
                        GameObject = new FsmGameObject { Value = proxy } };
                    read.TargetField.SetValue(read.Action, read.Target);
                }
                ValidateGuestEngineInputs(binding);
                binding.Rebind = false;
                foreach (var read in binding.Reads) _guestEngineInputReaders[read.Action] = binding;
                SyncEventLog.Record("guest-engine-inputs-bound", rule.ReaderPath + "::" + rule.Fsm + " " + rule.FamilyPrefix);
                return binding;
            }
            catch
            {
                RestoreGuestEngineInputReads(binding);
                _guestEngineInputs.Remove(binding);
                throw;
            }
        }

        private void UpdateGuestEngineInputValues(GuestEngineInputBinding binding)
        {
            var vars = binding.Data.FsmVariables;
            var installed = vars.FindFsmBool("Installed");
            foreach (var value in vars.BoolVariables) value.Value = value.Name == "Damaged";
            foreach (var value in vars.FloatVariables) value.Value = 0f;
            // Valves always takes two substrings before checking other engine
            // dependencies. Unavailable cams need a parseable neutral profile.
            foreach (var value in vars.StringVariables) value.Value = "00000000";
            var session = SessionManager.Instance;
            if (binding.Rule.CoolingAmbientSource != null)
            {
                var ambient = session != null && !session.IsHost && session.State == SessionState.Connected ? _engineBlockReplica.Inputs : null;
                if (ambient == null || !ambient.CoolingAmbientAvailable)
                    throw new GuestEngineInputsPendingException("Host cooling ambient temperature is not available yet.");
                vars.FindFsmFloat(binding.Rule.CoolingAmbientSource.Variable).Value = ambient.CoolingAmbientTemperature; return;
            }
            if (session == null || session.IsHost || session.State != SessionState.Connected) return;
            if (binding.Rule.CoolingAirflowSource != null)
            {
                var source = binding.Rule.CoolingAirflowSource; var airflow = _engineBlockReplica.Inputs;
                if (airflow == null || (airflow.CoolingAirflowFlags & (1 << source.Index)) == 0) return;
                if (source.Index != 1) vars.FindFsmFloat("CoolingAirRateModifier").Value = source.Index == 0 ? airflow.GrilleAirflow : source.Index == 2 ? airflow.HoodAirflow : airflow.FiberglassHoodAirflow;
                installed.Value = true; return;
            }
            if (binding.Rule.CoolantHoseSource != null)
            {
                var source = binding.Rule.CoolantHoseSource; var hoses = _engineBlockReplica.Inputs;
                if (hoses == null || (hoses.CoolantHoseFlags & (1 << source.Index)) == 0) return;
                vars.FindFsmFloat("Tightness").Value = hoses.CoolantHoseTightnessAt(source.Index); installed.Value = true; return;
            }
            if (binding.Rule.RadiatorSource != null)
            {
                var radiator = _engineBlockReplica.Inputs; if (radiator == null || !radiator.RadiatorInstalled) return;
                foreach (var value in vars.FloatVariables)
                    switch (value.Name)
                    {
                        case "Wear": value.Value = radiator.RadiatorWear; break;
                        case "Coolant": value.Value = radiator.RadiatorCoolant; break;
                        case "PressureCap": value.Value = radiator.RadiatorPressureCap; break;
                        case "FlectEfficiency": value.Value = radiator.RadiatorFlectEfficiency; break;
                    }
                installed.Value = true; return;
            }
            if (binding.Rule.RockerCoverSource != null)
            {
                var cover = _engineBlockReplica.Inputs; if (cover == null || !cover.RockerCoverInstalled) return;
                vars.FindFsmFloat("Tightness").Value = cover.RockerCoverTightness;
                installed.Value = true; return;
            }
            if (binding.Rule.OilpanSource != null)
            {
                var oilpan = _engineBlockReplica.Inputs; if (oilpan == null || !oilpan.OilpanInstalled) return;
                foreach (var value in vars.FloatVariables)
                    switch (value.Name)
                    {
                        case "Wear": value.Value = oilpan.OilpanWear; break;
                        case "Tightness": value.Value = oilpan.OilpanTightness; break;
                        case "Oil": value.Value = oilpan.Oil; break;
                        case "OilContamination": value.Value = oilpan.OilContamination; break;
                        case "OilViscosity": value.Value = oilpan.OilViscosity; break;
                    }
                installed.Value = true; return;
            }
            if (binding.Rule.ExhaustSource != null)
            {
                var source = binding.Rule.ExhaustSource; var exhaust = _engineBlockReplica.Inputs;
                if (exhaust == null || (exhaust.ExhaustFlags & (1 << source.Index)) == 0) return;
                installed.Value = true;
                foreach (var value in vars.FloatVariables)
                    value.Value = exhaust.ExhaustPerformanceAt(source.Index * 3 + (value.Name == "DataPower" ? 0 : value.Name == "DataTorque" ? 1 : 2));
                return;
            }
            if (binding.Rule.CarburettorSource != null || binding.Rule.AirCleanerSource != null)
            {
                bool airCleaner = binding.Rule.AirCleanerSource != null;
                var block = _engineBlockReplica.Inputs;
                if (block == null || (block.Flags & (airCleaner ? EngineBlockState.AirCleanerInstalled : EngineBlockState.CarburettorInstalled)) == 0) return;
                installed.Value = true;
                foreach (var value in vars.FloatVariables)
                    switch (value.Name)
                    {
                        case "Tightness": value.Value = block.CarburettorTightness; break;
                        case "FuelChamber": value.Value = block.FuelChamber; break;
                        case "CarbReserve": value.Value = block.CarbReserve; break;
                        case "SettingMixture": value.Value = block.SettingMixture; break;
                        case "DataPower": value.Value = airCleaner ? block.AirCleanerPower : block.CarburettorPower; break;
                        case "DataTorque": value.Value = airCleaner ? block.AirCleanerTorque : block.CarburettorTorque; break;
                        case "DataPowerAdd": value.Value = airCleaner ? block.AirCleanerPowerAdd : block.CarburettorPowerAdd; break;
                    }
                return;
            }
            if (binding.Rule.HeadSource != null)
            {
                var block = _engineBlockReplica.Inputs;
                installed.Value = block != null && (block.Flags & EngineBlockState.HeadInstalled) != 0;
                return;
            }
            if (binding.Rule.GearboxSource != null)
            {
                var gearbox = _gearboxReplica.Get();
                // Zero is a valid manual type. Until the host has a stable native
                // observation, pause Starter instead of bypassing its interlock.
                if (gearbox == null || (gearbox.Flags & GearboxState.Available) == 0)
                    throw new InvalidOperationException("Host gearbox starter input is not ready.");
                vars.FindFsmInt("Type").Value = gearbox.Type;
                return;
            }
            if (binding.Rule.BlockSource != null)
            {
                var block = _engineBlockReplica.Inputs;
                if (block == null || (block.Flags & EngineBlockState.Installed) == 0) return;
                installed.Value = true;
                var wear = vars.FindFsmFloat("Wear"); if (wear != null) wear.Value = block.Wear;
                var blockDamage = vars.FindFsmBool("Damaged"); if (blockDamage != null) blockDamage.Value = (block.Flags & EngineBlockState.Damaged) != 0;
                return;
            }
            if (binding.Rule.BatterySource != null)
            {
                var battery = _batteryReplica.Get();
                if (battery == null || (battery.Flags & BatteryState.Available) == 0) return;
                installed.Value = (battery.Flags & BatteryState.Installed) != 0;
                vars.FindFsmFloat("Charge").Value = battery.Charge;
                return;
            }
            if (binding.Rule.WiringSource != null)
            {
                var wire = _wiringReplica.Get(binding.Rule.WiringSource.Id);
                if (wire == null || (wire.Flags & WiringState.Available) == 0) return;
                installed.Value = (wire.Flags & WiringState.Installed) != 0;
                var boltedInput = vars.FindFsmBool("Bolted");
                if (boltedInput != null) boltedInput.Value = (wire.Flags & WiringState.Bolted) != 0;
                return;
            }
            var mount = binding.Source.Value;
            if (mount == null || !mount.activeInHierarchy || _replacementReplica == null) return;
            if (!TryMountAddress(mount.transform, out var kind, out uint parentId, out string path)) return;
            ReplacementBinding? selected = null;
            ReplacementPartState? state = null;
            uint selectedId = 0;
            var candidates = new HashSet<uint>(_pendingReplacements);
            foreach (uint id in _replacementParts.Keys) candidates.Add(id);
            foreach (uint id in candidates)
            {
                var candidate = _replacementReplica.GetAttached(id, kind, parentId, path);
                if (candidate == null) continue;
                if (state != null || !GuestEngineInputFamilyMatches(binding.Rule, candidate.FactoryId)) return;
                _replacementParts.TryGetValue(id, out selected);
                selectedId = id; state = candidate;
            }
            if (selected == null || state == null || !selected.Replica || selected.Factory.Failed || !selected.Factory.Suppressor.Active
                || selected.Factory.Rule.Identity.FactoryId != state.FactoryId
                || ResolveReplacementParent(state) != mount.transform
                || !selected.HasAppliedState || selected.AppliedRevision != state.Revision
                || _pendingReplacements.Contains(selectedId) || !selected.FittedPresentation
                || selected.Data == null || !selected.Data.gameObject.activeInHierarchy
                || selected.Data.transform.parent != mount.transform
                || !_bridge.PartIdentities.IsReplica(selected.Data)
                || selected.Data.FsmVariables.FindFsmString(SyncCatalog.ReplacementParts!["itemIdVariable"])?.Value != state.NativeId
                || !_bridge.PartIdentities.TryRootId(selected.Data, out uint currentId) || currentId != selectedId) return;
            var damaged = vars.FindFsmBool("Damaged");
            if (damaged != null)
            {
                if (!state.AlternatorDamaged.HasValue) return;
                damaged.Value = state.AlternatorDamaged.Value;
            }
            if (binding.Rule.SlotIndex != 0)
            {
                var c = SyncCatalog.ReplacementParts!;
                if (state.AssemblyId != binding.Rule.SlotIndex
                    || selected.Data.FsmVariables.FindFsmInt(c["assemblyVariable"])?.Value != state.AssemblyId
                    || selected.Data.FsmVariables.FindFsmString(c["slotReferenceVariable"])?.Value != selected.Factory.Rule.SlotReference) return;
                var bolted = vars.FindFsmBool("Bolted");
                if (bolted != null)
                {
                    int index = selected.Factory.Rule.Identity.TightnessIndex;
                    if (index < 0 || index >= state.Scalars.Length) throw new InvalidOperationException("Missing host rocker tightness.");
                    float tightness = _bridge.ReplacementTightness(selectedId, state.Scalars[index]);
                    bolted.Value = PartSlotPolicy.RockerBoltedInput(state, binding.Rule.SlotIndex, tightness);
                }
            }
            foreach (var value in vars.FloatVariables)
            {
                int index = Array.IndexOf(selected.Factory.Rule.Scalars, value.Name);
                if (index < 0 || index >= state.Scalars.Length)
                    throw new InvalidOperationException("Incomplete host engine input scalars.");
                float incoming = value.Name == "Tightness"
                    ? _bridge.ReplacementTightness(selectedId, state.Scalars[index]) : state.Scalars[index];
                if (float.IsNaN(incoming) || float.IsInfinity(incoming))
                    throw new InvalidOperationException("Invalid host engine input scalar.");
                value.Value = incoming;
            }
            foreach (var value in vars.StringVariables)
            {
                if (value.Name != selected.Factory.Rule.CamProfileVariable || !PartCamshaftPolicy.ValidProfile(state.CamProfile))
                    throw new InvalidOperationException("Incomplete host engine cam profile.");
                value.Value = state.CamProfile;
            }
            // Native calculation scratch values are shared between states. Only
            // normal GetFsm entries may read this data into those scratch variables.
            installed.Value = true;
        }

        private void ValidateGuestEngineInputMount(GuestEngineInputBinding binding)
        {
            var rule = binding.Rule;
            var mount = binding.Source.Value;
            // The block can move from StartParts into the car. Its factory and
            // consumer must agree on the live mount, independently of world path.
            if (mount == null || mount.name != rule.MountPath.Substring(rule.MountPath.LastIndexOf('/') + 1))
                throw new InvalidOperationException("Native engine input mount reference changed.");
            if (rule.WiringSource != null && ScenePath.Of(mount.transform) != rule.WiringSource.Path)
                throw new InvalidOperationException("Native engine wiring source changed.");
            if (rule.BatterySource != null && ScenePath.Of(mount.transform) != rule.BatterySource.Path)
                throw new InvalidOperationException("Native engine battery source changed.");
            if (rule.BlockSource != null && ScenePath.Of(mount.transform) != rule.BlockSource.Path)
                throw new InvalidOperationException("Native engine block source changed.");
            if (rule.CoolingAmbientSource != null && ScenePath.Of(mount.transform) != rule.CoolingAmbientSource.Path)
                throw new InvalidOperationException("Native cooling ambient source moved.");
            if (rule.CoolingAirflowSource != null && ScenePath.Of(mount.transform) != rule.CoolingAirflowSource.Mount.MountPath)
                throw new InvalidOperationException("Native cooling airflow mount moved.");
            if (rule.CoolantHoseSource != null && ScenePath.Of(mount.transform) != rule.CoolantHoseSource.Mount.MountPath)
                throw new InvalidOperationException("Native coolant hose input mount changed.");
            if (rule.RadiatorSource != null && ScenePath.Of(mount.transform) != rule.RadiatorSource.MountPath)
                throw new InvalidOperationException("Native radiator input mount changed.");
            var head = rule.RockerCoverSource ?? rule.OilpanSource ?? rule.HeadSource ?? rule.CarburettorSource ?? rule.AirCleanerSource ?? rule.ExhaustSource?.Mount;
            if (head != null && head.RootPrefix.Length != 0)
            {
                if (!GuestEngineProtection.MatchesMovingMount(mount.transform, head.RootPrefix, head.RelativePath)
                    || !GuestEngineProtection.NativeEnginePart(EngineBlockDataFsm(mount.transform.parent.gameObject, head.Fsm).FsmVariables.FindFsmString("ID")?.Value, head.RootPrefix))
                    throw new InvalidOperationException("Native engine input parent identity changed.");
            }
            if (rule.ExhaustSource != null && rule.ExhaustSource.Mount.RootPrefix.Length == 0 && ScenePath.Of(mount.transform) != rule.ExhaustSource.Mount.MountPath)
                throw new InvalidOperationException("Native exhaust input mount changed.");
            if (rule.GearboxSource != null && ScenePath.Of(mount.transform) != rule.GearboxSource.Path)
                throw new InvalidOperationException("Native engine gearbox source changed.");
            foreach (var family in rule.Families)
            {
                if (!_replacementFactories.TryGetValue(family.Identity.FactoryId, out var factory) || factory.Failed
                    || factory.Fsm == null || !ReferenceEquals(factory.Rule, family))
                    throw new InvalidOperationException("Engine input factory has not loaded.");
                if (rule.SlotIndex != 0)
                {
                    ValidateGuestEngineSlotTemplate(factory, rule);
                    if (GuestEngineSlotMount(rule, family) != mount)
                        throw new InvalidOperationException("Native engine input slot changed.");
                }
                else if (factory.Fsm.FsmVariables.FindFsmGameObject(rule.MountVariable)?.Value != mount)
                    throw new InvalidOperationException("Native engine input variants disagree on their mount.");
            }
            PlayMakerFSM? data = null;
            foreach (var fsm in mount.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == rule.InputFsm)
                {
                    if (data != null) throw new InvalidOperationException("Ambiguous native engine input mount.");
                    data = fsm;
                }
            if (data == null || rule.CoolingAmbientSource == null && data.FsmVariables.FindFsmBool("Installed") == null)
                throw new InvalidOperationException("Incomplete native engine input mount.");
            foreach (var reader in rule.Readers)
                if ((reader.ActionType == "GetFsmBool" && data.FsmVariables.FindFsmBool(reader.Variable) == null)
                    || (reader.ActionType == "GetFsmFloat" && data.FsmVariables.FindFsmFloat(reader.Variable) == null)
                    || (reader.ActionType == "GetFsmInt" && data.FsmVariables.FindFsmInt(reader.Variable) == null)
                    || (reader.ActionType == "GetFsmString" && data.FsmVariables.FindFsmString(reader.Variable) == null))
                    throw new InvalidOperationException("Missing native engine input scalar.");
        }

        private void ValidateGuestEngineInputs(GuestEngineInputBinding binding, string? readerPath = null)
        {
            var rule = binding.Rule;
            int floatCount = 0, stringCount = 0, intCount = 0, boolCount = 1;
            // Reuse only scratch storage; re-evaluate the current catalog and
            // live variables on every validation, including after a failed check.
            var names = binding.VariableNames;
            names.Clear(); names.Add("Installed");
            foreach (var read in rule.Readers)
            {
                if (!names.Add(read.Variable)) continue;
                if (read.ActionType == "GetFsmBool") boolCount++;
                if (read.ActionType == "GetFsmFloat") floatCount++;
                if (read.ActionType == "GetFsmString") stringCount++;
                if (read.ActionType == "GetFsmInt") intCount++;
            }
            if (binding.Fsm == null || binding.Fsm.FsmName != rule.Fsm
                || (readerPath ?? ScenePath.Of(binding.Fsm.transform)) != rule.ReaderPath
                || (!rule.DirectTarget && !ReferenceEquals(binding.Fsm.FsmVariables.FindFsmGameObject(rule.TargetVariable), binding.Source))
                || !GuestEngineInputActionsCurrent(binding) || binding.Proxy == null || binding.Data == null
                || binding.Data.gameObject != binding.Proxy || binding.Data.FsmName != rule.InputFsm
                || binding.Data.enabled || binding.Proxy.GetComponents<PlayMakerFSM>().Length != 1
                || binding.Data.Fsm.States.Length != 0 || binding.Data.FsmVariables.BoolVariables.Length != boolCount
                || binding.Data.FsmVariables.FloatVariables.Length != floatCount
                || binding.Data.FsmVariables.StringVariables.Length != stringCount
                || binding.Data.FsmVariables.IntVariables.Length != intCount
                || binding.Data.FsmVariables.FindFsmBool("Installed") == null)
                throw new InvalidOperationException("Guest engine input binding changed.");
            foreach (var read in rule.Readers)
                if ((read.ActionType == "GetFsmBool" && binding.Data.FsmVariables.FindFsmBool(read.Variable) == null)
                    || (read.ActionType == "GetFsmFloat" && binding.Data.FsmVariables.FindFsmFloat(read.Variable) == null)
                    || (read.ActionType == "GetFsmInt" && binding.Data.FsmVariables.FindFsmInt(read.Variable) == null)
                    || (read.ActionType == "GetFsmString" && binding.Data.FsmVariables.FindFsmString(read.Variable) == null))
                    throw new InvalidOperationException("Guest engine input proxy is incomplete.");
            ValidateGuestEngineInputMount(binding);
            foreach (var read in binding.Reads)
            {
                ValidateGuestEngineInputOwner(binding, read.Original);
                if (read.Target.OwnerOption != OwnerDefaultOption.SpecifyGameObject
                    || read.Target.GameObject == null || read.Target.GameObject.UseVariable
                    || read.Target.GameObject.Value != binding.Proxy)
                    throw new InvalidOperationException("Guest engine input proxy target changed.");
                ValidateGuestEngineInputRead(binding, read, read.Target);
            }
        }

        private static bool GuestEngineInputFamilyMatches(GuestEngineInputData rule, uint factoryId)
        {
            foreach (var family in rule.Families) if (family.Identity.FactoryId == factoryId) return true;
            return false;
        }

        private static void ValidateGuestEngineInputOwner(GuestEngineInputBinding binding, FsmOwnerDefault owner)
        {
            var source = owner.GameObject;
            if (owner.OwnerOption != OwnerDefaultOption.SpecifyGameObject || source == null
                || (binding.Rule.DirectTarget ? source.UseVariable || source.Name.Length != 0 || source.Value != binding.Source.Value
                    : !source.UseVariable || !ReferenceEquals(source, binding.Source)))
                throw new InvalidOperationException("Native engine input owner reference changed.");
        }

        private static void ValidateGuestEngineInputRead(GuestEngineInputBinding binding,
            GuestEngineInputRead read, FsmOwnerDefault target)
        {
            var action = read.Action;
            var rule = read.Rule;
            var fsmName = read.FsmNameField.GetValue(action) as FsmString;
            var variable = read.VariableNameField.GetValue(action) as FsmString;
            var output = rule.ActionType == "GetFsmBool"
                ? (NamedVariable?)binding.Fsm.FsmVariables.FindFsmBool(rule.Output)
                : rule.ActionType == "GetFsmString" ? (NamedVariable?)binding.Fsm.FsmVariables.FindFsmString(rule.Output)
                : rule.ActionType == "GetFsmInt" ? (NamedVariable?)binding.Fsm.FsmVariables.FindFsmInt(rule.Output)
                : binding.Fsm.FsmVariables.FindFsmFloat(rule.Output);
            if (!action.Enabled || read.State.Name != rule.State
                || !ReferenceEquals(read.TargetField.GetValue(action), target)
                || fsmName == null || fsmName.UseVariable || fsmName.Value != binding.Rule.InputFsm
                || variable == null || variable.UseVariable || variable.Value != rule.Variable
                || (bool)read.EveryFrameField.GetValue(action) != rule.EveryFrame
                || !ReferenceEquals(read.Output, output)
                || read.OutputField.FieldType != GuestEngineInputOutputType(rule.ActionType)
                || !ReferenceEquals(read.OutputField.GetValue(action), output))
                throw new InvalidOperationException("Native engine input signature changed at " + rule.State + ".");
        }

        private static Type GuestEngineInputOutputType(string actionType)
        {
            return actionType == "GetFsmBool" ? typeof(FsmBool) : actionType == "GetFsmString" ? typeof(FsmString)
                : actionType == "GetFsmInt" ? typeof(FsmInt) : typeof(FsmFloat);
        }

        private static bool GuestEngineInputActionsCurrent(GuestEngineInputBinding binding)
        {
            if (binding.Fsm == null || binding.Fsm.Fsm.States.Length != binding.States.Length) return false;
            for (int i = 0; i < binding.States.Length; i++)
                if (!ReferenceEquals(binding.Fsm.Fsm.States[i], binding.States[i])) return false;
            foreach (var read in binding.Reads)
                if (!FsmHook.IsNativeAction(read.State, read.Rule.ActionIndex, read.Action)) return false;
            return true;
        }

        private static FieldInfo GuestEngineInputField(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.FieldType != expected) throw new InvalidOperationException("Native engine input field changed: " + name);
            return field;
        }

        private static FsmState GuestEngineInputState(PlayMakerFSM fsm, string name)
        {
            FsmState? found = null;
            foreach (var state in fsm.Fsm.States)
                if (state.Name == name)
                {
                    if (found != null) throw new InvalidOperationException("Ambiguous native engine input state.");
                    found = state;
                }
            return found ?? throw new InvalidOperationException("Missing native engine input state.");
        }

        private static void RestoreGuestEngineInputReads(GuestEngineInputBinding binding)
        {
            binding.InputsReady = false;
            foreach (var read in binding.Reads)
                if (read.Target != null && ReferenceEquals(read.TargetField.GetValue(read.Action), read.Target))
                    read.TargetField.SetValue(read.Action, read.Original);
            foreach (var read in binding.Reads)
                if (binding.Proxy != null && read.TargetField.GetValue(read.Action) is FsmOwnerDefault owner
                    && owner.GameObject != null && owner.GameObject.Value == binding.Proxy)
                    throw new InvalidOperationException("Changed native reader still references owned engine inputs.");
            if (binding.Proxy != null) UnityEngine.Object.Destroy(binding.Proxy);
        }

        private void RestoreGuestEngineInputs()
        {
            _guestEngineInputsRestoring = true;
            foreach (var valve in new List<GuestValveBinding>(_guestValveInputs))
                try { RestoreGuestValveInputs(valve); }
                catch (Exception error) { if (valve.Fsm != null) GuestEngineProtection.Fail(valve.Fsm, error); else GuestEngineProtection.NoteFailure("restoring valve readers", error); }

            foreach (var binding in new List<GuestEngineInputBinding>(_guestEngineInputs))
                try
                {
                    if (!_guestEngineInputOriginalsReady)
                        throw new InvalidOperationException("Saved guest parts could not be restored; retaining engine input protection until restart.");
                    RestoreGuestEngineInputReads(binding);
                    _guestEngineInputs.Remove(binding);
                }
                catch (Exception error)
                {
                    if (binding.Fsm != null) GuestEngineProtection.Fail(binding.Fsm, error);
                    else GuestEngineProtection.NoteFailure("restoring engine input readers", error);
                }
            if (_guestEngineInputs.Count == 0 && _guestValveInputs.Count == 0 && _guestEngineInputOriginalsReady)
            {
                if (GuestEngineProtection.PrepareInputs == _prepareGuestEngineInputs)
                    GuestEngineProtection.PrepareInputs = null;
                _prepareGuestEngineInputs = null;
                _guestEngineInputScope = null;
                _guestEngineInputReaders.Clear();
            }
        }
    }
}
