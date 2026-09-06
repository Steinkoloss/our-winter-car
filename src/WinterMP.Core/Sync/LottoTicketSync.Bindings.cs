using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;

namespace WinterMP.Core.Sync
{
    internal sealed partial class LottoTicketSync
    {
        private sealed class Hook
        {
            public FsmState State = null!;
            public FsmStateAction[] Original = new FsmStateAction[0];
        }
        private readonly List<Hook> _hooks = new List<Hook>();

        private sealed class RequestAction : FsmStateAction
        {
            private readonly Action _request;
            public RequestAction(Action request) { _request = request; }
            public override void OnEnter() { _request(); }
        }

        private sealed class ClaimGateAction : FsmStateAction
        {
            private readonly LottoTicketSync _owner;
            private readonly FsmStateAction[] _native;
            public ClaimGateAction(LottoTicketSync owner, FsmStateAction[] native) { _owner = owner; _native = native; }
            public override void OnEnter()
            {
                bool lotto = _owner.QueueClaim();
                foreach (var action in _native) action.Enabled = !lotto;
                // A held action prevents FINISHED -> Turn into garbage until the host
                // answers. Megaveto continues through its original actions unchanged.
                if (!lotto) Finish();
            }
        }

        private void Locate()
        {
            if (_ready) return;
            SyncCatalog.EnsureLoaded();
            _c = SyncCatalog.LottoTickets;
            var c = _c;
            if (c == null || SyncCatalog.Banking == null) throw new InvalidOperationException("Missing Lotto ticket catalog.");
            foreach (var obj in ScenePath.ScanFsms())
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                string path = ScenePath.Of(fsm.transform);
                if (path == c["payPath"] && fsm.FsmName == c["payFsm"]) _pay = fsm;
                if (path == c["spawnerPath"] && fsm.FsmName == c["spawnerFsm"]) _spawner = fsm;
                if (path == c["claimPath"] && fsm.FsmName == c["claimFsm"]) _claim = fsm;
            }
            if (_pay == null || _spawner == null || _claim == null) return;
            _payFailure.Suppress(_pay); _claimFailure.Suppress(_claim);
            foreach (var fsm in new[] { _pay, _spawner, _claim }) Init(fsm);
            _cash = Float(FsmVariables.GlobalVariables, SyncCatalog.Banking.CashGlobal);
            _bank = Float(FsmVariables.GlobalVariables, SyncCatalog.Banking.BankGlobal);
            _total = Float(_pay.FsmVariables, c["payTotal"]);
            _payRound = Int(_pay, c["payRound"]);
            _counter = Int(_spawner, c["counter"]); _saveId = String(_spawner, c["saveId"]);
            _prefab = ObjectVar(_spawner, c["prefab"]).Value;
            _spawn = ObjectVar(_spawner, c["spawnPoint"]).Value;
            _database = ObjectVar(_spawner, c["database"]).Value;
            _paper = ObjectVar(_spawner, c["paper"]).Value;
            _claimObject = ObjectVar(_claim, c["claimObject"]);
            if (_prefab == null || _spawn == null || _database == null || _paper == null) return;
            var lists = Lines(_spawner);
            if (lists == null) return;
            _selection = lists;
            BindActions();
            _ready = true;
            _payFailure.Restore(); _claimFailure.Restore();
            WinterMPPlugin.Log.LogInfo("Lotto tickets: native form, persistent factory and claim box bound.");
        }

        private void BindActions()
        {
            var c = _c;
            if (c == null || _pay == null || _claim == null || _spawner == null || SyncCatalog.Banking == null) return;
            var request = State(_pay, c["requestState"]);
            var commit = State(_pay, c["commitState"]);
            var claim = State(_claim, c["claimState"]);
            Require(request, "FloatCompare");
            Require(commit, "MasterAudioPlaySound", "FloatSubtract", "SetFsmInt", "SendEventByName", "Wait");
            Require(claim, "GetFsmFloat", "FloatCompare", "FloatClamp", "FloatAdd");
            if (Field<FsmFloat>(request.Actions[0], "float1").Name != c["payTotal"]
                || Field<FsmFloat>(request.Actions[0], "float2").Name != SyncCatalog.Banking.CashGlobal
                || Field<FsmFloat>(commit.Actions[1], "floatVariable").Name != SyncCatalog.Banking.CashGlobal
                || Field<FsmFloat>(commit.Actions[1], "subtract").Name != c["payTotal"]
                || Field<FsmInt>(commit.Actions[2], "setValue").Name != c["payRound"]
                || Field<FsmString>(claim.Actions[0], "variableName").Value != c["winnings"]
                || Field<FsmFloat>(claim.Actions[1], "float2").Value != c.BankThreshold
                || Field<FsmFloat>(claim.Actions[3], "floatVariable").Name != SyncCatalog.Banking.CashGlobal)
                throw new InvalidOperationException("Lotto payment/claim action arguments changed.");
            foreach (string key in new[] { "commitState", "closeState", "fundsState" })
                if (!FsmHook.EnsureRemoteEntry(_pay, c[key])) throw new InvalidOperationException("Missing Lotto pay state: " + c[key]);
            if (!FsmHook.EnsureRemoteEntry(_claim, c["claimReset"])) throw new InvalidOperationException("Missing claim reset.");
            // Keep native receipt sound and closing delay; remove all local accounting/spawn.
            Replace(commit, new[] { commit.Actions[0], commit.Actions[4] });
            Replace(request, new FsmStateAction[] { new RequestAction(QueueBuy) });
            var actions = claim.Actions;
            var guarded = new FsmStateAction[actions.Length + 1]; guarded[0] = new ClaimGateAction(this, actions);
            Array.Copy(actions, 0, guarded, 1, actions.Length); Replace(claim, guarded);
        }

        private void Replace(FsmState state, FsmStateAction[] actions)
        {
            _hooks.Add(new Hook { State = state, Original = state.Actions }); state.Actions = actions;
        }
        private static void Init(PlayMakerFSM fsm) { if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm); }
        private static FsmState State(PlayMakerFSM fsm, string name)
            => FsmHook.FindState(fsm, name) ?? throw new InvalidOperationException("Missing Lotto state: " + name);
        private static T Field<T>(object action, string name) where T : class
            => action.GetType().GetField(name)?.GetValue(action) as T ?? throw new InvalidOperationException("Missing Lotto action field: " + name);
        private static void Require(FsmState state, params string[] types)
        {
            if (state.Actions.Length != types.Length) throw new InvalidOperationException("Lotto action count changed: " + state.Name);
            for (int i = 0; i < types.Length; i++)
                if (state.Actions[i].GetType().Name != types[i] || !state.Actions[i].Enabled)
                    throw new InvalidOperationException("Lotto action changed: " + state.Name + "/" + i);
        }
        private static FsmFloat Float(FsmVariables vars, string name)
        {
            foreach (var v in vars.FloatVariables) if (v.Name == name) return v;
            throw new InvalidOperationException("Missing Lotto float: " + name);
        }
        private static FsmInt Int(PlayMakerFSM fsm, string name)
        {
            foreach (var v in fsm.FsmVariables.IntVariables) if (v.Name == name) return v;
            throw new InvalidOperationException("Missing Lotto integer: " + name);
        }
        private static FsmString String(PlayMakerFSM fsm, string name)
        {
            foreach (var v in fsm.FsmVariables.StringVariables) if (v.Name == name) return v;
            throw new InvalidOperationException("Missing Lotto string: " + name);
        }
        private static FsmGameObject ObjectVar(PlayMakerFSM fsm, string name)
        {
            foreach (var v in fsm.FsmVariables.GameObjectVariables) if (v.Name == name) return v;
            throw new InvalidOperationException("Missing Lotto object: " + name);
        }
        private static PlayMakerFSM Fsm(GameObject obj, string name)
        {
            foreach (var fsm in obj.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name) { Init(fsm); return fsm; }
            throw new InvalidOperationException("Missing Lotto FSM: " + name);
        }
        private IList[]? Lines(PlayMakerFSM fsm)
        {
            if (_c == null) return null;
            var result = new IList[3];
            for (int i = 0; i < 3; i++)
            {
                IList? list = null;
                foreach (var component in fsm.GetComponents<MonoBehaviour>())
                {
                    if (component == null || component.GetType().Name != "PlayMakerArrayListProxy") continue;
                    var type = component.GetType();
                    if (type.GetField("referenceName")?.GetValue(component) as string != _c["line" + (i + 1)]) continue;
                    if (list != null) throw new InvalidOperationException("Duplicate Lotto line.");
                    list = type.GetProperty("arrayList")?.GetValue(component, null) as IList;
                }
                if (list == null) return null;
                if (list.IsReadOnly || list.IsFixedSize) throw new InvalidOperationException("Lotto line is not writable.");
                result[i] = list;
            }
            return result;
        }

        public void Clear()
        {
            try
            {
                _payFailure.Restore(); _claimFailure.Restore();
                if (_c != null)
                {
                    if (_pay != null && _pay.ActiveStateName == _c["requestState"]) Enter(_pay, "closeState");
                    if (_claim != null && _claim.ActiveStateName == _c["claimState"]) Enter(_claim, "claimReset");
                }
                foreach (var ticket in _tickets.Values)
                {
                    _items.UnregisterTicket(ticket.State.TicketId);
                    if (ticket.GuestClone && ticket.Object != null) UnityEngine.Object.Destroy(ticket.Object);
                }
                foreach (var obj in _hiddenLocalTickets) if (obj != null) obj.SetActive(true);
            }
            catch (Exception e) { WinterMPPlugin.Log.LogWarning("Lotto ticket teardown: " + e.Message); }
            finally
            {
                foreach (var hook in _hooks)
                {
                    try
                    {
                        foreach (var action in hook.Original) action.Enabled = true;
                        hook.State.Actions = hook.Original;
                    }
                    catch (Exception e) { WinterMPPlugin.Log.LogWarning("Lotto hook restore: " + e.Message); }
                }
                _hooks.Clear(); _payFailure.Restore(); _claimFailure.Restore();
                _tickets.Clear(); _hiddenLocalTickets.Clear(); _replica.Clear(); _ledger.Clear(); _requestAt.Clear();
                _pending = null; _c = null; _pay = _spawner = _claim = null;
                _prefab = _spawn = _database = _paper = null; _cash = _bank = _total = null;
                _counter = _payRound = null; _saveId = null; _claimObject = null; _selection = new IList[0];
                _probeAt = _scanAt = _sendAt = _keepaliveAt = _retryAt = 0; _ready = _failed = _resetClaim = false;
            }
        }
    }
}
