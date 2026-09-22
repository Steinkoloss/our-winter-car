using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class TaxiMeterBinding
    {
        internal static readonly string[] ValueNames = { "Price", "BaseCost", "OdoTrip", "OdoTotal", "IncomeTotal", "IncomeReceipts", "Odo100meters", "Interval", "MpS" };
        private readonly TaxiMeterData _c;
        private readonly bool _guest;
        private readonly PlayMakerFSM _job, _meter, _knob, _button, _display, _ring;
        private readonly GameObject _car, _indicators, _lightIndicator;
        private readonly Transform _modeKnob;
        private readonly Renderer _dome;
        private readonly Material _lightOn, _lightOff;
        private readonly TextMesh _modeText, _costText;
        private readonly FsmFloat[] _values = new FsmFloat[TaxiMeterState.ValueCount];
        private readonly FsmInt _rotation;
        private readonly List<Action> _restore = new List<Action>(), _resume = new List<Action>();
        private readonly List<PlayMakerFSM> _paused = new List<PlayMakerFSM>();
        private readonly TaxiMeterIntentOrder _order = new TaxiMeterIntentOrder();
        private uint _revision, _controlRevision = 1;
        private bool _restored;

        internal static TaxiMeterBinding? TryBind(SessionManager session, Action<TaxiMeterAction> send)
        {
            SyncCatalog.EnsureLoaded();
            var c = SyncCatalog.TaxiMeter ?? throw new InvalidOperationException("Missing taxi meter catalog.");
            PlayMakerFSM? job = null, meter = null;
            foreach (var obj in ScenePath.ScanFsms())
            {
                var f = obj as PlayMakerFSM; if (f == null) continue;
                string path = ScenePath.Of(f.transform);
                if (path == c["jobPath"] && f.FsmName == c["jobFsm"]) Assign(ref job, f);
                if (path == c["meterPath"] && f.FsmName == c["meterFsm"]) Assign(ref meter, f);
            }
            if (job == null || meter == null) return null;
            Init(job); Init(meter);
            return new TaxiMeterBinding(c, job, meter, !session.IsHost, send);
        }

        private TaxiMeterBinding(TaxiMeterData c, PlayMakerFSM job, PlayMakerFSM meter, bool guest, Action<TaxiMeterAction> send)
        {
            _c = c; _job = job; _meter = meter; _guest = guest; _car = Obj(job, "Car");
            _knob = Fsm(Child(meter.transform, c["knob"]).gameObject, c["knobFsm"]);
            _button = Fsm(Child(meter.transform, c["button"]).gameObject, c["buttonFsm"]);
            _display = Fsm(Child(meter.transform, c["display"]).gameObject, c["displayFsm"]);
            _ring = Fsm(Obj(_knob, "CarPhone"), "Ring");
            _indicators = Obj(_knob, "Indicators"); _lightIndicator = Obj(_button, "Indicator");
            _dome = Need(Obj(_button, "LightDome").GetComponent<Renderer>());
            _lightOn = Need(_button.FsmVariables.FindFsmMaterial("LightOn")?.Value);
            _lightOff = Need(_button.FsmVariables.FindFsmMaterial("LightOff")?.Value);
            _rotation = Need(_knob.FsmVariables.FindFsmInt("RotationInt"));
            _modeText = Need(Field<FsmProperty>(ActionAt(_knob, "State 3", 0, "SetProperty"), "targetProperty").TargetObject.Value as TextMesh);
            _costText = Need(Field<FsmProperty>(ActionAt(_display, "Convert", 1, "SetProperty"), "targetProperty").TargetObject.Value as TextMesh);
            _modeKnob = Need(Field<FsmOwnerDefault>(ActionAt(_knob, c["select"], 6, "SetRotation"), "gameObject").GameObject.Value).transform;
            for (int i = 0; i < _values.Length; i++) _values[i] = Need(meter.FsmVariables.FindFsmFloat(ValueNames[i]));
            Validate();
            try
            {
                if (guest) InstallGuest(send);
                else
                {
                    foreach (string state in new[] { c["increase"], c["decrease"], c["select"], "State 9" }) Observe(_knob, state);
                    foreach (string state in new[] { c["toggle"], c["reset"], "State 2", "State 5" }) Observe(_button, state);
                    PrepareEntry(_knob, c["increase"]); PrepareEntry(_knob, c["decrease"]);
                    PrepareEntry(_button, c["toggle"]); PrepareEntry(_button, c["reset"]);
                    InstallSpeedReads();
                }
                WinterMPPlugin.Log.LogInfo("Taxi meter bound: " + (guest ? "shared display and guest controls" : "native host fare"));
            }
            catch { Restore(); throw; }
        }

        private void Validate()
        {
            if (Obj(_knob, "Tripmeter") != _meter.gameObject || Obj(_button, "Tripmeter") != _meter.gameObject
                || Obj(_button, "Knob") != _knob.gameObject || Obj(_knob, "LightButton") != _button.gameObject
                || !_modeKnob.IsChildOf(_meter.transform) || !_meter.transform.IsChildOf(_car.transform))
                throw new InvalidOperationException("Changed taxi meter object links.");
            var rotation = ActionAt(_knob, _c["select"], 6, "SetRotation");
            if (Field<FsmFloat>(rotation, "yAngle").Value != 0 || Field<FsmFloat>(rotation, "zAngle").Value != 0
                || Convert.ToInt32(Field<object>(rotation, "space")) != 1)
                throw new InvalidOperationException("Changed taxi knob pose.");
            foreach (string state in new[] { _c["increase"], _c["decrease"] })
            {
                var add = ActionAt(_knob, state, 1, "IntAdd"); var clamp = ActionAt(_knob, state, 2, "IntClamp");
                if (!ReferenceEquals(Field<FsmInt>(add, "intVariable"), _rotation)
                    || Field<FsmInt>(add, "add").Value != (state == _c["increase"] ? 35 : -35)
                    || !ReferenceEquals(Field<FsmInt>(clamp, "intVariable"), _rotation)
                    || Field<FsmInt>(clamp, "minValue").Value != 0 || Field<FsmInt>(clamp, "maxValue").Value != 210)
                    throw new InvalidOperationException("Changed taxi mode increment.");
            }
            if (!ReferenceEquals(Field<FsmBool>(ActionAt(_button, _c["toggle"], 2, "BoolFlip"), "boolVariable"), Bool(_button, "TaxiLightOn")))
                throw new InvalidOperationException("Changed taxi light toggle.");
            if (Field<FsmString>(ActionAt(_button, _c["reset"], 0, "SendEventByName"), "sendEvent").Value != "HARDRESET")
                throw new InvalidOperationException("Changed taxi total reset.");
            string[] resets = { "OdoTrip", "OdoTotal", "IncomeTotal", "IncomeReceipts", "Price" };
            for (int i = 0; i < resets.Length; i++)
            {
                var a = ActionAt(_knob, "State 9", i, "SetFsmFloat");
                if (Field<FsmString>(a, "variableName").Value != resets[i] || Field<FsmFloat>(a, "setValue").Value != 0
                    || Field<FsmOwnerDefault>(a, "gameObject").GameObject.Value != _meter.gameObject)
                    throw new InvalidOperationException("Changed taxi reset output.");
            }
            foreach (string name in new[] { "On", "Off" }) Bool(_meter, name);
            Bool(_knob, "ActivateCustomer"); Bool(_knob, "On"); Bool(_ring, "DriverBreak");
        }

        internal TaxiMeterState Capture()
        {
            int rotation = _rotation.Value;
            if (rotation < 0 || rotation > 210 || rotation % 35 != 0) throw new InvalidOperationException("Invalid native taxi mode.");
            var pose = _modeKnob.localRotation;
            var s = new TaxiMeterState { KnobRotation = new NetQuaternion(pose.x, pose.y, pose.z, pose.w), Revision = ++_revision, ControlRevision = _controlRevision, Mode = (byte)(rotation / 35), Display = _costText.text, ModeDisplay = _modeText.text };
            int stage = Need(_job.FsmVariables.FindFsmInt("JobStage")).Value;
            if (_car.activeInHierarchy && (stage == 2 || stage == 3) && Ready(_knob) && Ready(_button)) s.Flags |= TaxiMeterState.Available;
            if (Bool(_meter, "On").Value) s.Flags |= TaxiMeterState.MeterOn;
            if (Bool(_meter, "Off").Value) s.Flags |= TaxiMeterState.MeterOff;
            if (Bool(_button, "TaxiLightOn").Value) s.Flags |= TaxiMeterState.TaxiLight;
            if (Bool(_knob, "ActivateCustomer").Value) s.Flags |= TaxiMeterState.CustomerEnabled;
            if (Bool(_knob, "On").Value) s.Flags |= TaxiMeterState.KnobOn;
            if (Bool(_ring, "DriverBreak").Value) s.Flags |= TaxiMeterState.DriverBreak;
            if (_indicators.activeSelf) s.Flags |= TaxiMeterState.Indicators;
            if (_lightIndicator.activeSelf) s.Flags |= TaxiMeterState.LightIndicator;
            for (int i = 0; i < _values.Length; i++) s.Values[i] = _values[i].Value;
            return s;
        }
        internal void Act(SessionManager session, TaxiMeterIntent intent, byte actor)
        {
            bool near = GamblingSync.TryPlayerPosition(session, actor, out var position) && (position - _meter.transform.position).sqrMagnitude <= 9;
            bool busy = (_knob.ActiveStateName != _c["knobIdle"] && _knob.ActiveStateName != "Get scroll")
                || (_button.ActiveStateName != _c["buttonIdle"] && _button.ActiveStateName != "Wait button");
            if (intent.ExpectedControlRevision != _controlRevision || !_order.Accept(actor, intent.Sequence) || !TaxiMeterPolicy.CanAct(Capture(), intent, actor, near, busy))
            { SyncEventLog.Record("taxi-meter-rejected", actor + "/" + intent.Sequence + "/" + intent.Action); return; }
            bool knob = intent.Action == TaxiMeterAction.IncreaseMode || intent.Action == TaxiMeterAction.DecreaseMode;
            string state = intent.Action == TaxiMeterAction.IncreaseMode ? _c["increase"] : intent.Action == TaxiMeterAction.DecreaseMode ? _c["decrease"]
                : intent.Action == TaxiMeterAction.ToggleLight ? _c["toggle"] : _c["reset"];
            _controlRevision = unchecked(_controlRevision + 1);
            FsmHook.FireRemoteEntry(knob ? _knob : _button, state);
            SyncEventLog.Record("taxi-meter", actor + "/" + intent.Sequence + "/" + intent.Action);
        }
        internal void Forget(byte actor)
        {
            _order.Forget(actor);
            // A newly admitted connection must not revive a previous connection's
            // delayed request after its per-player sequence has been reset.
            if (!_guest) _controlRevision = unchecked(_controlRevision + 1);
        }
        private void Observe(PlayMakerFSM fsm, string name)
        {
            var state = State(fsm, name); var old = state.Actions;
            if (!FsmHook.OnStateEnter(fsm, name, () => _controlRevision = unchecked(_controlRevision + 1))) throw new InvalidOperationException("Cannot observe taxi controls.");
            var installed = state.Actions; _restore.Add(() => { if (ReferenceEquals(state.Actions, installed)) state.Actions = old; });
        }
        private void PrepareEntry(PlayMakerFSM fsm, string name)
        {
            var oldEvents = fsm.Fsm.Events; var oldTransitions = fsm.Fsm.GlobalTransitions;
            if (!FsmHook.EnsureRemoteEntry(fsm, name)) throw new InvalidOperationException("Cannot enter taxi control.");
            var events = fsm.Fsm.Events; var transitions = fsm.Fsm.GlobalTransitions;
            _restore.Add(() => { if (ReferenceEquals(fsm.Fsm.Events, events)) fsm.Fsm.Events = oldEvents;
                if (ReferenceEquals(fsm.Fsm.GlobalTransitions, transitions)) fsm.Fsm.GlobalTransitions = oldTransitions; });
        }
        internal void Restore()
        {
            if (_restored) return; _restored = true;
            for (int i = _restore.Count - 1; i >= 0; i--) try { _restore[i](); } catch (Exception e) { WinterMPPlugin.Log.LogWarning("Taxi meter restore: " + e.Message); }
            foreach (var resume in _resume) resume(); _restore.Clear(); _resume.Clear();
        }
        private static bool Ready(PlayMakerFSM fsm) => fsm.enabled && fsm.gameObject.activeInHierarchy && fsm.Fsm.Started;
        private static void Assign(ref PlayMakerFSM? target, PlayMakerFSM value) { if (target != null) throw new InvalidOperationException("Ambiguous taxi meter."); target = value; }
        private static void Init(PlayMakerFSM fsm) { if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm); }
        private static T Need<T>(T? value) where T : class => value ?? throw new InvalidOperationException("Missing taxi meter binding.");
        private static Transform Child(Transform root, string path) => Need(root.Find(path));
        private static GameObject Obj(PlayMakerFSM fsm, string name) => Need(fsm.FsmVariables.FindFsmGameObject(name)?.Value);
        private static FsmBool Bool(PlayMakerFSM fsm, string name) => Need(fsm.FsmVariables.FindFsmBool(name));
        private static PlayMakerFSM Fsm(GameObject go, string name)
        {
            PlayMakerFSM? found = null; foreach (var f in go.GetComponents<PlayMakerFSM>()) if (f.FsmName == name) Assign(ref found, f);
            Init(Need(found)); return found!;
        }
        private static FsmState State(PlayMakerFSM fsm, string name) => Need(FsmHook.FindState(fsm, name));
        private static FsmStateAction ActionAt(PlayMakerFSM fsm, string state, int index, string type)
        {
            var a = Need(FsmHook.NativeAction(State(fsm, state), index));
            if (a.GetType().Name != type) throw new InvalidOperationException("Changed taxi meter action " + state + "/" + index);
            return a;
        }
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
    }
}
