using System;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class AtfFillerBinding
    {
        private sealed class Slot
        {
            internal FsmState State = null!;
            internal FsmStateAction[] Actions = null!;
            internal FsmStateAction Action = null!;
            internal int Index;
            internal bool Enabled;
        }

        private readonly AtfRefillData _rule;
        private readonly bool _guest;
        private readonly SyncedItem _vehicle;
        private readonly List<Slot> _suppressed = new List<Slot>();
        private readonly PlayMakerFSM _mount, _cap, _fill, _gauge;
        private readonly FsmFloat _rotation, _step, _fillOil, _gaugeOil, _gaugeMax, _gaugeScale;
        private readonly GameObject _mesh, _gui, _sound;
        private readonly FsmState _up, _down;
        private readonly FsmStateAction[] _upActions, _downActions;
        private FsmStateAction[]? _upHooks, _downHooks;
        private readonly FsmStateAction _render, _gaugeCalculate, _gaugeRender;
        private readonly float _oldRotation, _oldFillOil, _oldGaugeOil, _oldGaugeMax, _oldGaugeScale;
        private readonly bool _oldCapActive, _oldMesh, _oldFillActive, _oldSound;
        private readonly Transform[] _guiSubtree;
        private readonly bool[] _oldGuiActive;
        private readonly Vector3 _oldGaugeSize, _oldCapLocalPosition;
        private readonly Quaternion _oldMeshRotation, _oldCapLocalRotation;
        private readonly GameObject? _oldFillSource;
        private readonly bool _oldFillPouring;
        private readonly string _oldFillName;
        private bool _restored, _received, _available;
        private float _receivedOil;
        private Vector3 _receivedCapPosition;
        private Quaternion _receivedCapRotation;

        internal Transform CapTransform => _cap.transform;
        internal Vector3 CapLocalPosition => VehicleTransform.InverseTransformPoint(_cap.transform.position);
        internal Quaternion CapLocalRotation => Quaternion.Inverse(VehicleTransform.rotation) * _cap.transform.rotation;
        private Transform VehicleTransform => _vehicle.Body != null ? _vehicle.Body.transform
            : throw new InvalidOperationException("ATF vehicle body is unavailable.");
        internal SphereCollider FillCollider { get; }
        internal float Rotation => _rotation.Value;
        internal bool Open => !_restored && Rotation <= 1f && FillCollider.gameObject.activeInHierarchy;
        internal bool Mounted => ValidateMounted();
        internal float OilLevel
        {
            get
            {
                if (_guest) return _received && _available ? _receivedOil : throw new InvalidOperationException("Host ATF oil is unavailable.");
                if (!ValidateMounted()) throw new InvalidOperationException("The host automatic gearbox is not mounted.");
                return Float(_mount, _rule["oil"]).Value;
            }
            set
            {
                if (_guest || !Finite(value) || value < -1f || value > 6.3f || !ValidateMounted())
                    throw new InvalidOperationException("Invalid host ATF destination.");
                var part = PartData();
                // Native Update 2 mirrors once a second. Mirror this one scalar
                // immediately as well so an immediate save cannot lose a refill.
                Float(_mount, _rule["oil"]).Value = value;
                Float(part, _rule["oil"]).Value = value;
            }
        }

        internal static AtfFillerBinding Bind(SyncedItem vehicle, AtfRefillData rule, bool guest, Action<byte> onCapTurn)
        {
            var binding = new AtfFillerBinding(vehicle, rule, guest);
            try { binding.Install(onCapTurn); return binding; }
            catch { binding.Restore(); throw; }
        }

        private AtfFillerBinding(SyncedItem vehicle, AtfRefillData rule, bool guest)
        {
            _vehicle = vehicle; _rule = rule; _guest = guest;
            if (!vehicle.IsVehicle || vehicle.Body == null || vehicle.Path != rule["rootPath"]
                || ScenePath.Of(vehicle.Body.transform) != rule["rootPath"])
                throw new InvalidOperationException("ATF vehicle identity changed.");
            _mount = Find(vehicle.Body.transform, rule["mountPath"], rule["dataFsm"]);
            _cap = Find(vehicle.Body.transform, rule["capPath"], rule["capFsm"]);
            _fill = Find(vehicle.Body.transform, rule["fillPath"], rule["fillFsm"]);
            _gauge = Find(null, rule["gaugePath"], rule["gaugeFsm"]);
            // Init loads action metadata and variable references; it does not
            // start a state, activate an object, or run its native actions.
            foreach (var fsm in new[] { _cap, _fill, _gauge }) if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
            _rotation = Float(_cap, rule["rotation"]); _step = Float(_cap, rule["step"]);
            _fillOil = Float(_fill, rule["fillOil"]);
            _gaugeOil = Float(_gauge, rule["gaugeFluid"]); _gaugeMax = Float(_gauge, rule["gaugeMax"]);
            _gaugeScale = Float(_gauge, rule["gaugeScale"]);
            _mesh = Object(_cap, rule["mesh"]).Value;
            _gui = Object(_cap, rule["capGui"]).Value;
            if (_mesh == null || _gui == null || _mesh.transform.parent != _cap.transform
                || _cap.transform.parent != _mount.transform || _cap.GetComponentsInChildren<Rigidbody>(true).Length != 0
                || Object(_cap, rule["capTrigger"]).Value != _fill.gameObject
                || _fill.transform.parent != _cap.transform || Object(_fill, rule["fillGearbox"]).Value != _mount.gameObject
                || Object(_fill, rule["fillGui"]).Value != _gui || Object(_gauge, rule["gaugeTarget"]).Value != _mount.gameObject
                || _gauge.transform.parent != _gui.transform)
                throw new InvalidOperationException("Native ATF object references changed.");
            FillCollider = _fill.GetComponent<SphereCollider>();
            if (FillCollider == null || !FillCollider.enabled || !FillCollider.isTrigger || FillCollider.radius != .03f
                || FillCollider.center != Vector3.zero || _fill.transform.localScale != Vector3.one)
                throw new InvalidOperationException("Native ATF filler geometry changed.");
            _up = State(_cap, rule["up"]); _down = State(_cap, rule["down"]);
            _upActions = _up.Actions; _downActions = _down.Actions;
            ValidateCap();
            _render = ActionAt(_up, 2, "SetRotation");
            _sound = ValidateFill();
            ValidateGauge();
            _gaugeCalculate = ActionAt(State(_gauge, rule["gaugeState"]), 2, "FloatOperator");
            _gaugeRender = ActionAt(State(_gauge, rule["gaugeState"]), 3, "SetScale");
            _oldRotation = _rotation.Value; _oldMeshRotation = _mesh.transform.localRotation;
            _oldCapLocalPosition = _cap.transform.localPosition; _oldCapLocalRotation = _cap.transform.localRotation;
            _oldCapActive = _cap.gameObject.activeSelf; _oldMesh = _mesh.activeSelf; _oldFillActive = _fill.gameObject.activeSelf;
            _guiSubtree = _gui.GetComponentsInChildren<Transform>(true);
            _oldGuiActive = new bool[_guiSubtree.Length];
            for (int i = 0; i < _guiSubtree.Length; i++) _oldGuiActive[i] = _guiSubtree[i].gameObject.activeSelf;
            _oldSound = _sound.activeSelf;
            _oldFillOil = _fillOil.Value; _oldGaugeOil = _gaugeOil.Value; _oldGaugeMax = _gaugeMax.Value;
            _oldGaugeScale = _gaugeScale.Value; _oldGaugeSize = _gauge.transform.localScale;
            _oldFillSource = Object(_fill, rule["capTrigger"]).Value;
            _oldFillPouring = _fill.FsmVariables.FindFsmBool(rule["fillPouring"]).Value;
            _oldFillName = _fill.FsmVariables.FindFsmString(rule["fillName"]).Value;
        }

        private void Install(Action<byte> onCapTurn)
        {
            Suppress(State(_fill, _rule["pour"]), 0);
            Suppress(State(_fill, _rule["pour"]), 2);
            Suppress(State(_fill, _rule["pour"]), 3);
            Suppress(State(_fill, _rule["stop"]), 0);
            if (_guest)
            {
                Suppress(State(_fill, _rule["pour"]), 4);
                Suppress(State(_gauge, _rule["gaugeState"]), 0);
                Suppress(State(_gauge, _rule["gaugeState"]), 1);
                _upHooks = new FsmStateAction[] { new FsmHookAction(() => { if (!_restored && _received && _available) onCapTurn(3); }) };
                _downHooks = new FsmStateAction[] { new FsmHookAction(() => { if (!_restored && _received && _available) onCapTurn(2); }) };
                StopActions(_up); StopActions(_down);
                _up.Actions = _upHooks; _down.Actions = _downHooks;
                _upHooks[0].Init(_up); _downHooks[0].Init(_down);
            }
            Tick();
        }

        internal bool ValidateMounted()
        {
            if (_restored || _vehicle.Body == null || _mount == null || _cap == null || _fill == null
                || !_mount.gameObject.activeInHierarchy || !_mount.Fsm.Initialized || !_mount.Fsm.Started) return false;
            if (_guest) return _received && _available;
            var type = _mount.FsmVariables.FindFsmInt(_rule["type"]);
            if (type == null || type.Value < 2 || !_mount.enabled || !_cap.enabled || !_cap.Fsm.Started || _mount.ActiveStateName != _rule["mountReady"]
                || _mount.FsmVariables.FindFsmBool(_rule["installed"])?.Value != true
                || !_cap.gameObject.activeInHierarchy) return false;
            var part = Object(_mount, _rule["activePart"]).Value;
            if (part == null || !part.activeInHierarchy || part.transform.parent != _mount.transform) return false;
            var data = FsmOn(part, _rule["dataFsm"]);
            if (!data.Fsm.Initialized || !data.Fsm.Started
                || data.FsmVariables.FindFsmInt(_rule["assembly"])?.Value != 1
                || data.FsmVariables.FindFsmInt(_rule["type"])?.Value != type.Value) return false;
            var oil = Float(_mount, _rule["oil"]); var saved = Float(data, _rule["oil"]);
            if (ReferenceEquals(oil, saved) || !Finite(oil.Value) || oil.Value < -1 || oil.Value > 6.3f
                || Float(_mount, _rule["oilMax"]).Value != 6.3f) return false;
            var copy = ActionAt(State(_mount, _rule["mountReady"]), 1, "SetFsmFloat");
            External(copy, _mount, _rule["activePart"], _rule["dataFsm"], _rule["oil"], false);
            Variable(copy, "setValue", oil);
            return true;
        }

        private PlayMakerFSM PartData() => FsmOn(Object(_mount, _rule["activePart"]).Value, _rule["dataFsm"]);

        internal void Turn(bool unscrew)
        {
            if (_guest || !ValidateMounted()) throw new InvalidOperationException("Cannot turn an unavailable host ATF cap.");
            ValidateCap();
            var actions = unscrew ? _downActions : _upActions;
            actions[0].OnEnter(); actions[1].OnEnter(); actions[2].OnEnter();
            bool open = _rotation.Value <= 1f;
            var presentation = State(_cap, _rule[open ? "open" : "closed"]);
            // Avoid the host's mouse loop and shared GUI state for remote input.
            ActionAt(presentation, 0, "MasterAudioPlaySound").OnEnter();
            ActionAt(presentation, 1, "ActivateGameObject").OnEnter();
            ActionAt(presentation, 2, "ActivateGameObject").OnEnter();
        }

        internal void Apply(AtfFillerState state)
        {
            if (!_guest || _restored || !AtfPolicy.Valid(state) || state.VehicleId != _vehicle.Id)
                throw new InvalidOperationException("Invalid ATF filler projection.");
            _receivedCapPosition = state.CapLocalPosition.ToUnity();
            var rotation = state.CapLocalRotation.ToUnity();
            float inverseLength = 1f / Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y
                + rotation.z * rotation.z + rotation.w * rotation.w);
            _receivedCapRotation = new Quaternion(rotation.x * inverseLength, rotation.y * inverseLength,
                rotation.z * inverseLength, rotation.w * inverseLength);
            _received = true; _available = (state.Flags & 1) != 0; _receivedOil = state.OilLevel;
            _rotation.Value = state.Rotation;
            if (_available && !_cap.gameObject.activeSelf) _cap.gameObject.SetActive(true);
            _render.OnEnter();
            _mesh.SetActive(!_available || state.Rotation > 1f);
            _fill.gameObject.SetActive(_available && state.Rotation <= 1f);
            if (!_available) { _gui.SetActive(false); _sound.SetActive(false); }
            Tick();
        }

        internal void Tick()
        {
            if (_restored) return;
            foreach (var slot in _suppressed)
            {
                if (slot.State.Actions != slot.Actions || slot.Actions[slot.Index] != slot.Action)
                    throw new InvalidOperationException("Native ATF transfer action identity changed.");
                slot.Action.Enabled = false;
                if (slot.State.ActiveActions.Contains(slot.Action)) slot.Action.Finish();
            }
            if (!_guest) return;
            if (_up.Actions != _upHooks || _down.Actions != _downHooks)
                throw new InvalidOperationException("Guest ATF cap hook identity changed.");
            if (_received)
            {
                ProjectCapPose();
                // The native dormant cap's first Start entry makes its mesh
                // visible. Reassert after that entry as well as on receipt.
                bool mesh = !_available || _rotation.Value > 1f;
                bool fill = _available && _rotation.Value <= 1f;
                if (_mesh.activeSelf != mesh) _mesh.SetActive(mesh);
                if (_fill.gameObject.activeSelf != fill) _fill.gameObject.SetActive(fill);
            }
            _fillOil.Value = _gaugeOil.Value = _received && _available ? _receivedOil : 0;
            _gaugeMax.Value = 6.3f;
            _gaugeCalculate.OnEnter(); _gaugeRender.OnEnter();
        }

        private void ProjectCapPose()
        {
            if (_cap == null || _cap.transform.parent != _mount.transform)
                throw new InvalidOperationException("Native ATF cap parent changed.");
            var root = VehicleTransform;
            // The engine has its own hinged body, which can differ on a guest
            // with another saved assembly. Align only its transient cap endpoint
            // after vehicle smoothing; native engine physics and saves stay local.
            _cap.transform.position = root.TransformPoint(_receivedCapPosition);
            _cap.transform.rotation = root.rotation * _receivedCapRotation;
        }

        internal void SetPourGauge(bool show)
        {
            if (_restored) return;
            show &= Mounted;
            if (show)
            {
                _gaugeOil.Value = OilLevel; _gaugeMax.Value = 6.3f;
                _gaugeCalculate.OnEnter(); _gaugeRender.OnEnter();
                // Native Mouse off 2 recursively hides the bar and meshes.
                // Replace the suppressed pour action's recursive activation,
                // confined to the existing ATF subtree and without its ancestors.
                foreach (var node in _guiSubtree)
                    if (node != null && !node.gameObject.activeSelf) node.gameObject.SetActive(true);
            }
            if (_gui.activeSelf != show) _gui.SetActive(show);
        }

        internal void Restore()
        {
            if (_restored) return;
            _restored = true;
            if (_guest)
            {
                RestorePiece(() =>
                {
                    // Leave the remote pour while its writes are still removed.
                    // The native FINISHED chain reaches the idle trigger state.
                    if (_fill != null && _fill.enabled && _fill.gameObject.activeInHierarchy && _fill.Fsm.Started
                        && _fill.ActiveStateName != _rule["fillIdle"]) _fill.SendEvent("FINISHED");
                });
                RestorePiece(() => { if (_upHooks != null) { StopActions(_up); _up.Actions = _upActions; } });
                RestorePiece(() => { if (_downHooks != null) { StopActions(_down); _down.Actions = _downActions; } });
                RestorePiece(() => { _rotation.Value = _oldRotation; if (_mesh != null) _mesh.transform.localRotation = _oldMeshRotation; });
                RestorePiece(() => { if (_cap != null) { _cap.transform.localPosition = _oldCapLocalPosition;
                    _cap.transform.localRotation = _oldCapLocalRotation; } });
                RestorePiece(() => { _fillOil.Value = _oldFillOil; _gaugeOil.Value = _oldGaugeOil; _gaugeMax.Value = _oldGaugeMax; _gaugeScale.Value = _oldGaugeScale; });
                RestorePiece(() => { Object(_fill, _rule["capTrigger"]).Value = _oldFillSource;
                    _fill.FsmVariables.FindFsmBool(_rule["fillPouring"]).Value = _oldFillPouring;
                    _fill.FsmVariables.FindFsmString(_rule["fillName"]).Value = _oldFillName; });
                RestorePiece(() => { if (_gauge != null) _gauge.transform.localScale = _oldGaugeSize; });
                RestorePiece(() => { if (_mesh != null) _mesh.SetActive(_oldMesh); });
                RestorePiece(() => { if (_fill != null) _fill.gameObject.SetActive(_oldFillActive); });
                for (int i = 0; i < _guiSubtree.Length; i++)
                {
                    int index = i;
                    RestorePiece(() => { if (_guiSubtree[index] != null) _guiSubtree[index].gameObject.SetActive(_oldGuiActive[index]); });
                }
                RestorePiece(() => { if (_sound != null) _sound.SetActive(_oldSound); });
                RestorePiece(() => { if (_cap != null) _cap.gameObject.SetActive(_oldCapActive); });
            }
            foreach (var slot in _suppressed) RestorePiece(() => slot.Action.Enabled = slot.Enabled);
            foreach (var slot in _suppressed) RestorePiece(() => ResumeNativeUpdate(slot));
        }

        private static void ResumeNativeUpdate(Slot slot)
        {
            var fsm = slot.State.Fsm;
            if (!slot.Enabled || !fsm.Active || !fsm.Started || fsm.ActiveState != slot.State
                || slot.Action.GetType().GetField("everyFrame")?.GetValue(slot.Action) is not bool eachFrame || !eachFrame
                || slot.State.ActiveActions.Contains(slot.Action)) return;
            // FsmStateAction.Finish removes an action from ActiveActions. Restore
            // its native scheduling without running a saved write during cleanup.
            slot.Action.Finished = false; slot.Action.Entered = true; slot.Action.Active = true;
            int index = 0;
            while (index < slot.State.ActiveActions.Count && Array.IndexOf(slot.State.Actions, slot.State.ActiveActions[index]) < slot.Index) index++;
            slot.State.ActiveActions.Insert(index, slot.Action);
        }

        private static void RestorePiece(Action restore)
        {
            try { restore(); }
            catch (Exception error) { WinterMPPlugin.Log.LogWarning("ATF native state restore failed: " + error.Message); }
        }

        private void Suppress(FsmState state, int index)
        {
            var action = state.Actions[index];
            _suppressed.Add(new Slot { State = state, Actions = state.Actions, Index = index, Action = action, Enabled = action.Enabled });
            action.Enabled = false;
            if (state.ActiveActions.Contains(action)) action.Finish();
        }

        private static void StopActions(FsmState state)
        { foreach (var action in new List<FsmStateAction>(state.ActiveActions)) action.Finish(); }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
