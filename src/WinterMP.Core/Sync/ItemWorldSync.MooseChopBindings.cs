using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class MooseSection
        {
            internal PlayMakerFSM Fsm = null!;
            internal FsmInt Pieces = null!;
            internal FsmGameObject Spawnpoint = null!;
            internal FsmState Gate = null!;
            internal FsmStateAction[] Actions = null!;
            internal int SavedPieces;
            internal bool Enabled;
        }
        private sealed class MooseBody
        {
            internal Rigidbody Body = null!;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal bool Kinematic;
        }
        private sealed class MooseBinding
        {
            internal Transform Root = null!;
            internal Transform? Parent, Mover;
            internal PlayMakerFSM? Hit;
            internal FsmState? Death;
            internal FsmStateAction[]? DeathActions;
            internal bool Active, MoverActive, Guest, Failed;
            internal uint Epoch, Revision;
            internal readonly MooseSection[] Sections = new MooseSection[2];
            internal readonly MooseBody[] Bodies = new MooseBody[MooseCorpseState.BodyCount];
            internal MooseCorpseState? Received;
            internal MooseChopIntent? Pending;
            internal float NextSend, RetryAt, PendingUntil, DeathRetryAt, DeathUntil;
        }
        private MooseBinding? _moose;
        private bool _mooseFailed;
        private float _nextMooseProbe;
        private static uint _mooseEpoch;

        private void RefreshMooseChop()
        {
            var c = SyncCatalog.MooseChop; var session = SessionManager.Instance;
            if (c == null || session == null || _meatFactory == null || _meatFailed || _mooseFailed || _moose != null
                || Time.unscaledTime < _nextMooseProbe) return;
            _nextMooseProbe = Time.unscaledTime + 2;
            try
            {
                PlayMakerFSM? front = null, hit = null;
                foreach (var obj in ScenePath.ScanFsms())
                {
                    var fsm = obj as PlayMakerFSM;
                    if (fsm == null) continue;
                    string path = ScenePath.Of(fsm.transform);
                    if (fsm.FsmName == c["fsm"] && (path == c["path"] || path == c["detached"]))
                    {
                        if (front != null) throw new InvalidOperationException("Ambiguous native moose corpse.");
                        front = fsm;
                    }
                    if (fsm.FsmName == c["hitFsm"] && path == c["hitPath"]) hit = fsm;
                }
                if (front == null) return;
                var m = new MooseBinding { Root = front.transform, Parent = front.transform.parent, Hit = hit,
                    Active = front.gameObject.activeSelf, Guest = !session.IsHost };
                _moose = m;
                if (hit != null)
                {
                    for (var t = hit.transform; t != null; t = t.parent)
                        if (ScenePath.Of(t) == c["mover"]) m.Mover = t;
                    if (m.Mover == null) throw new InvalidOperationException("Native moose mover missing.");
                    m.MoverActive = m.Mover.gameObject.activeSelf;
                    if (!hit.Fsm.Initialized) hit.Fsm.Init(hit);
                    m.Death = MooseState(hit, c["death"], "SetParent", "ActivateGameObject", "DestroyObject");
                    var da = m.Death.Actions;
                    if (MooseTarget(hit, da[0]) != m.Root.gameObject || MooseTarget(hit, da[1]) != m.Root.gameObject
                        || PackageField<FsmGameObject>(da[2], "gameObject")?.Value != m.Mover.gameObject)
                        throw new InvalidOperationException("Native moose death targets changed.");
                    if (!FsmHook.EnsureRemoteEntry(hit, c["death"]) || !FsmHook.EnsureRemoteEntry(hit, c["hitIdle"]))
                        throw new InvalidOperationException("Native moose death states changed.");
                    m.DeathActions = da;
                }
                var bodies = m.Root.GetComponentsInChildren<Rigidbody>(true);
                if (bodies.Length != m.Bodies.Length) throw new InvalidOperationException("Native moose ragdoll changed.");
                for (int i = 0; i < m.Bodies.Length; i++)
                {
                    var t = i == 0 ? m.Root : m.Root.Find(c["body" + i]);
                    var body = t != null ? t.GetComponent<Rigidbody>() : null;
                    if (body == null) throw new InvalidOperationException("Native moose body missing.");
                    m.Bodies[i] = new MooseBody { Body = body, Position = t!.localPosition, Rotation = t.localRotation, Kinematic = body.isKinematic };
                }
                for (byte i = 0; i < 2; i++)
                {
                    var t = i == 0 ? m.Root : m.Root.Find(c["rear"]);
                    PlayMakerFSM? fsm = null;
                    foreach (var member in t.GetComponents<PlayMakerFSM>()) if (member.FsmName == c["fsm"]) fsm = member;
                    if (fsm == null) throw new InvalidOperationException("Native moose section missing.");
                    if (!fsm.Fsm.Initialized) fsm.Fsm.Init(fsm);
                    var gate = MooseState(fsm, c["pieces"], "IntCompare");
                    var compare = MooseState(fsm, c["compare"], "GameObjectCompare");
                    var create = MooseState(fsm, c["create"], "IntAdd", "SetFsmGameObject", "SendEventByName", "Wait");
                    MooseState(fsm, c["idle"], "CollisionEvent");
                    MooseState(fsm, c["cooldown"], "SetGameObject", "Wait");
                    MooseState(fsm, c["sound"], "RandomInt", "ConvertIntToString", "BuildStringFast", "MasterAudioPlaySound");
                    var pieces = fsm.FsmVariables.FindFsmInt(c["pieces"]);
                    var spawn = fsm.FsmVariables.FindFsmGameObject(c["spawnpoint"]);
                    var axe = PackageField<FsmGameObject>(compare.Actions[0], "compareTo")?.Value;
                    var limit = PackageField<FsmInt>(gate.Actions[0], "integer2");
                    var spawnEvent = PackageField<FsmEventTarget>(create.Actions[2], "eventTarget");
                    if (pieces == null || spawn?.Value == null || axe == null || ScenePath.Of(axe.transform) != c["axe"]
                        || PackageField<FsmInt>(gate.Actions[0], "integer1")?.Name != c["pieces"]
                        || limit == null || limit.UseVariable || limit.Value != 4
                        || !MooseTransition(gate, PackageField<FsmEvent>(gate.Actions[0], "lessThan"), c["create"])
                        || !MooseTransition(gate, PackageField<FsmEvent>(gate.Actions[0], "equal"), c["cooldown"])
                        || !MooseTransition(gate, PackageField<FsmEvent>(gate.Actions[0], "greaterThan"), c["cooldown"])
                        || !MooseTransition(compare, PackageField<FsmEvent>(compare.Actions[0], "equalEvent"), c["sound"])
                        || !MooseTransition(compare, PackageField<FsmEvent>(compare.Actions[0], "notEqualEvent"), c["cooldown"])
                        || ScenePath.Of(spawn.Value.transform) != c["axeCollider"]
                        || PackageField<FsmOwnerDefault>(compare.Actions[0], "gameObjectVariable")?.GameObject.Name != c["object"]
                        || fsm.FsmVariables.FindFsmGameObject(c["spawner"])?.Value != _meatFactory.gameObject
                        || PackageField<FsmInt>(create.Actions[0], "intVariable")?.Name != c["pieces"]
                        || PackageField<FsmInt>(create.Actions[0], "add")?.Value != 1
                        || MooseTarget(fsm, create.Actions[1]) != _meatFactory.gameObject
                        || PackageField<FsmString>(create.Actions[1], "variableName")?.Value != c["factorySpawnpoint"]
                        || PackageField<FsmGameObject>(create.Actions[1], "setValue")?.Name != c["spawnpoint"]
                        || spawnEvent == null || (int)spawnEvent.target != 2
                        || fsm.Fsm.GetOwnerDefaultTarget(spawnEvent.gameObject) != _meatFactory.gameObject
                        || spawnEvent.fsmName.Value != _meatFactory.FsmName
                        || PackageField<FsmString>(create.Actions[2], "sendEvent")?.Value != "SPAWNITEM"
                        || PackageField<FsmFloat>(create.Actions[2], "delay")?.Value != 0)
                        throw new InvalidOperationException("Native axe/meat action bindings changed.");
                    foreach (string key in new[] { "pieces", "cooldown", "idle" })
                        if (!FsmHook.EnsureRemoteEntry(fsm, c[key])) throw new InvalidOperationException("Native chop transition missing.");
                    var section = new MooseSection { Fsm = fsm, Pieces = pieces, SavedPieces = pieces.Value, Spawnpoint = spawn,
                        Gate = gate, Actions = gate.Actions, Enabled = fsm.enabled };
                    m.Sections[i] = section;
                }
                if (m.Guest)
                {
                    for (byte i = 0; i < 2; i++)
                    {
                        byte section = i;
                        m.Sections[i].Gate.Actions = new FsmStateAction[] { new FsmHookAction(() => QueueMooseChop(section)) };
                    }
                    if (m.Death != null) m.Death.Actions = new FsmStateAction[] { new FsmHookAction(QueueMooseDeath) };
                }
                else { m.Epoch = ++_mooseEpoch; if (m.Epoch == 0) m.Epoch = ++_mooseEpoch; }
                SyncEventLog.Record("moose-chop-bind", m.Guest ? "guest" : "host");
            }
            catch (Exception e) { FailMooseChop(e); }
        }

        private static FsmState MooseState(PlayMakerFSM fsm, string name, params string[] types)
        {
            var state = FsmHook.FindState(fsm, name) ?? throw new InvalidOperationException("Native moose state missing: " + name);
            if (state.Actions.Length != types.Length) throw new InvalidOperationException("Native moose actions changed: " + name);
            for (int i = 0; i < types.Length; i++)
                if (!state.Actions[i].Enabled || state.Actions[i].GetType().Name != types[i])
                    throw new InvalidOperationException("Native moose action changed: " + name + "/" + i);
            return state;
        }
        private static GameObject? MooseTarget(PlayMakerFSM fsm, FsmStateAction action)
        {
            var owner = PackageField<FsmOwnerDefault>(action, "gameObject");
            return owner == null ? null : fsm.Fsm.GetOwnerDefaultTarget(owner);
        }
        private static bool MooseTransition(FsmState state, FsmEvent? e, string target)
        {
            if (e == null) return false;
            foreach (var transition in state.Transitions)
                if (transition.EventName == e.Name && transition.ToState == target) return true;
            return false;
        }
        private void FailMooseChop(Exception e)
        {
            if (_mooseFailed) return;
            _mooseFailed = true;
            if (_moose != null)
            {
                _moose.Failed = true;
                foreach (var section in _moose.Sections) if (section?.Fsm != null) section.Fsm.enabled = false;
            }
            WinterMPPlugin.Log.LogWarning("Moose chopping disabled: " + e.Message);
            SyncEventLog.Record("moose-chop-disabled", e.Message);
        }
        private void ClearMooseChop()
        {
            var m = _moose;
            if (m != null)
            {
                if (m.Guest && m.Root != null)
                {
                    m.Root.gameObject.SetActive(false); m.Root.parent = m.Parent;
                    foreach (var saved in m.Bodies)
                    {
                        if (saved?.Body == null) continue;
                        saved.Body.transform.localPosition = saved.Position; saved.Body.transform.localRotation = saved.Rotation;
                        saved.Body.isKinematic = saved.Kinematic;
                    }
                    if (m.Mover != null) m.Mover.gameObject.SetActive(m.MoverActive);
                    m.Root.gameObject.SetActive(m.Active);
                }
                foreach (var section in m.Sections)
                {
                    if (section?.Fsm == null) continue;
                    section.Gate.Actions = section.Actions;
                    section.Fsm.enabled = section.Enabled;
                    if (m.Guest) section.Pieces.Value = section.SavedPieces;
                    if (m.Guest && section.Fsm.Fsm.Started) FsmHook.FireRemoteEntry(section.Fsm, SyncCatalog.MooseChop!["idle"]);
                }
                if (m.Death != null && m.DeathActions != null) m.Death.Actions = m.DeathActions;
            }
            _moose = null; _mooseFailed = false; _nextMooseProbe = 0;
        }
    }
}
