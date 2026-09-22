using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class SparkplugFittingChecks
    {
        internal static IEnumerator RunLifecycle(Action<string, Action> check)
        {
            var snapshots = new List<ReplacementPartState>();
            using (var f = new LifecycleFixture())
            {
                for (int slot = 1; slot <= 4; slot++)
                {
                    int selected = slot;
                    f.Loose(slot);
                    snapshots.Add(f.State());
                    var oldBody = f.Base.Data.GetComponent<Rigidbody>();
                    var install = f.Request(PartFitOperation.Install, (byte)slot);
                    check("sparkplug lifecycle: socket " + selected + " starts native fitting once", () =>
                    {
                        Require(f.Execute(install) == PartFitStatus.Pending, "Native fitting did not start: " + f.Describe());
                        Require(f.Base.Mounts[selected - 1].ActiveStateName == "Install 2" && oldBody != null
                            && f.Base.Data.transform.parent != f.Base.Mounts[selected - 1].transform,
                            "Probe did not observe the deferred physics boundary: " + f.Describe());
                        Require(Mathf.Abs(f.Mass.Value - 100.125f) < .0001f, "Native installation did not add exactly one plug mass.");
                        Require(f.Execute(install) == PartFitStatus.Pending && Mathf.Abs(f.Mass.Value - 100.125f) < .0001f,
                            "Pending retry repeated native installation.");
                    });
                    float until = Time.realtimeSinceStartup + .35f;
                    while (Time.realtimeSinceStartup < until) { yield return null; f.Process(); }
                    check("sparkplug lifecycle: socket " + selected + " settles on the host and clears loose ownership", () =>
                    {
                        var state = f.State();
                        Require(f.Status(install) == PartFitStatus.Accepted && state.Installed && state.AssemblyId == selected
                            && PartAttachmentPolicy.HasAttachment(state) && oldBody == null && f.Base.Data.GetComponent<Rigidbody>() == null,
                            "Native fitting did not settle: " + f.Describe());
                        Require(f.Base.Data.transform.parent == f.Base.Mounts[selected - 1].transform
                            && f.Base.Data.transform.localPosition.sqrMagnitude < .000001f && f.Base.Pick.isTrigger
                            && f.Base.Data.gameObject.layer == 12 && f.Base.Screw.enabled, "Fitted physics/pick did not settle: " + f.Describe());
                        Require(!((IDictionary)Get(f.Base.Sync, "_items")).Contains(f.Id), "Fitted part retained its old loose ownership.");
                        Require(f.Execute(install) == PartFitStatus.Accepted && Mathf.Abs(f.Mass.Value - 100.125f) < .0001f,
                            "Accepted retry repeated the mass change.");
                    });
                    check("sparkplug lifecycle: socket " + selected + " carries wear through native mount updates", () =>
                    {
                        var mount = f.Base.Mounts[selected - 1];
                        Require(mount.FsmVariables.FindFsmFloat("Wear").Value == 93
                            && mount.FsmVariables.FindFsmFloat("Durability").Value == .75f, "Installation lost plug condition.");
                        mount.FsmVariables.FindFsmFloat("Wear").Value = 87;
                    });
                    snapshots.Add(f.State());
                    yield return null;
                    check("sparkplug lifecycle: socket " + selected + " completes a guarded tighten and loosen cycle", () =>
                    {
                        Require(f.Base.Data.FsmVariables.FindFsmFloat("Wear").Value == 87, "Native fitted wear writer did not run.");
                        Require(f.Execute(f.Request(PartFitOperation.ToolTighten)) == PartFitStatus.Accepted && !f.State().RemovalAllowed,
                            "Fitted plug did not tighten: " + f.Describe());
                        snapshots.Add(f.State());
                        Require(f.Execute(f.Request(PartFitOperation.Remove)) == PartFitStatus.Bolted, "Tight plug was removable.");
                        var control = Get(f.Base.Part, "ToolScrew"); Set(control, "NextRequestAt", 0f);
                        Require(f.Execute(f.Request(PartFitOperation.ToolLoosen)) == PartFitStatus.Accepted && f.State().RemovalAllowed,
                            "Native loosening did not make the plug removable: " + f.Describe());
                    });
                    var removal = f.Request(PartFitOperation.Remove);
                    var position = f.Base.Data.transform.position;
                    check("sparkplug lifecycle: socket " + selected + " restores native loose physics exactly once", () =>
                    {
                        Require(f.Execute(removal) == PartFitStatus.Pending, "Removal request was rejected: " + f.Describe());
                        var body = f.Base.Data.GetComponent<Rigidbody>();
                        if (body == null) throw new InvalidOperationException("Native removal did not recreate physics: " + f.Describe());
                        Require(!body.isKinematic && body.detectCollisions && Mathf.Abs(body.mass - .125f) < .0001f
                            && body.collisionDetectionMode == CollisionDetectionMode.Continuous, "Native removal did not restore its rigidbody.");
                        Require((body.velocity - f.Velocity.Value).sqrMagnitude < .000001f
                            && (body.position - position).sqrMagnitude < .000001f && body.transform.parent == null,
                            "Native removal lost world pose or car velocity.");
                        Require(!f.Base.Pick.isTrigger && f.Base.Pick.enabled && f.Base.Data.gameObject.tag == "PART"
                            && f.Base.Data.gameObject.layer == 19 && !f.Base.Screw.enabled, "Loose interaction did not return.");
                        Require(Mathf.Abs(f.Mass.Value - 100f) < .0001f && f.Base.Data.FsmVariables.FindFsmFloat("Wear").Value == 87,
                            "Removal lost condition or subtracted mass incorrectly.");
                        Require(f.Execute(removal) == PartFitStatus.Pending && ReferenceEquals(body, f.Base.Data.GetComponent<Rigidbody>())
                            && Mathf.Abs(f.Mass.Value - 100f) < .0001f, "Pending retry recreated physics or subtracted mass again.");
                    });
                    f.Process();
                    check("sparkplug lifecycle: socket " + selected + " republishes the same loose identity with a fresh lease", () =>
                    {
                        var state = f.State();
                        Require(f.Status(removal) == PartFitStatus.Accepted && !state.Installed && state.AssemblyId == 0
                            && !PartAttachmentPolicy.HasAttachment(state) && state.NativeId == "SPRKPLUG071", "Removal did not settle: " + f.Describe());
                        var item = ((IDictionary)Get(f.Base.Sync, "_items"))[f.Id];
                        Require(item != null && !(bool)Get(item, "LocallyOwned") && (byte)Get(item, "RemoteOwner") == byte.MaxValue
                            && ReferenceEquals(Get(item, "Body"), f.Base.Data.GetComponent<Rigidbody>()), "Removal inherited a stale physics lease.");
                        Require(f.Execute(removal) == PartFitStatus.Accepted && f.Base.Data.GetComponents<Rigidbody>().Length == 1,
                            "Accepted retry duplicated the loose body.");
                    });
                    snapshots.Add(f.State());
                }
            }
            RunGuestLifecycle(check, snapshots);
        }

        private sealed class LifecycleFixture : IDisposable
        {
            internal readonly Fixture Base = new Fixture("sparkplug-lifecycle-probe.json");
            internal readonly FsmFloat Mass = new FsmFloat { Name = "MotorMass", UseVariable = true, Value = 100 };
            internal readonly FsmVector3 Velocity = new FsmVector3 { Name = "CarVelocity", UseVariable = true, Value = new Vector3(2, .5f, -1) };
            internal readonly uint Id;
            private readonly SessionManager _savedSession, _session;
            private readonly RemotePlayer _player;
            private uint _sequence;

            internal LifecycleFixture()
            {
                _savedSession = SessionManager.Instance!;
                _session = Base.Root.AddComponent<SessionManager>(); _session.enabled = false;
                Property(_session, "IsHost", true); Property(_session, "State", SessionState.Hosting); Property(_session, "LocalPlayerId", (byte)0);
                typeof(SessionManager).GetProperty("Instance", Static).GetSetMethod(true).Invoke(null, new object[] { _session });
                _player = new RemotePlayer { PlayerId = 1, Peer = new PeerId(445) };
                ((IDictionary)Get(_session, "_playersByPeer")).Add(_player.Peer, _player);
                var rows = ReadToolRows("sparkplug-lifecycle-probe.json");
                var parent = NativeBagPartChecks.MakeFsm(Base.Root, NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Data"));
                parent.FsmVariables.FindFsmString("ID").Value = "VIN11101";
                parent.FsmVariables.FindFsmString("UTAssemblyID").Value = "VIN11101AID"; parent.FsmVariables.FindFsmString("UTPos").Value = "VIN11101POS";
                parent.FsmVariables.FindFsmInt("AssemblyID").Value = 1;
                parent.Fsm.Init(parent); NativeBagPartChecks.Start(parent);
                PartIdentity.TryItemId("VIN11101", out uint parentId); ((IDictionary)Get(Base.Sync, "_nativeParts")).Add(parentId, parent);
                PartIdentity.TryItemId("SPRKPLUG071", out Id); ((IDictionary)Get(Base.Sync, "_replacementParts")).Add(Id, Base.Part);
                var functions = NativeBagPartChecks.MakeFsm(Base.Installer.gameObject, NativeBagPartChecks.Find(rows, "CORRIS/AssembyDatabase", "Functions"));
                functions.Fsm.Init(functions); NativeBagPartChecks.Start(functions); NativeBagPartChecks.Fire(functions, "Idle");
                // Functions only supplies sound/UI feedback; retain its event graph
                // as the boundary while the mount executes every physics action.
                foreach (var mount in Base.Mounts)
                {
                    mount.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Rigidbody", UseVariable = true, ObjectType = typeof(Rigidbody) } };
                    var objects = new List<FsmGameObject>(mount.FsmVariables.GameObjectVariables);
                    objects.RemoveAll(v => v.Name == "AssemblyDatabase"); objects.Add(new FsmGameObject { Name = "AssemblyDatabase", UseVariable = true, Value = Base.Installer.gameObject });
                    mount.FsmVariables.GameObjectVariables = objects.ToArray();
                    var floats = new List<FsmFloat>(mount.FsmVariables.FloatVariables); floats.RemoveAll(v => v.Name == "MotorMass"); floats.Add(Mass); mount.FsmVariables.FloatVariables = floats.ToArray();
                    mount.FsmVariables.Vector3Variables = new[] { Velocity };
                    mount.FsmVariables.FindFsmGameObject("db_Installed1").Value = Base.Prerequisite.gameObject;
                    NativeBagPartChecks.LoadActions(mount, NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1110/" + mount.gameObject.name, "Data"),
                        "Idle", "Allow install?", "Far", "Near", "Install 1", "Install 2", "Installed", "Update 2", "Allow removal?", "Remove part");
                }
                NativeBagPartChecks.LoadActions(Base.Data, NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Data"), "Idle", "Install 2");
                Base.Screw.FsmVariables.ObjectVariables = new[] { new FsmObject { Name = "Collider", UseVariable = true, ObjectType = typeof(BoxCollider), Value = Base.Pick } };
                NativeBagPartChecks.LoadActions(Base.Screw, NativeBagPartChecks.Find(rows, "SPRKPLUG0", "Screw"), "Setup 2", "State 1", "State 2");
                Base.Screw.Fsm.StartState = "Setup 2";
                var body = Base.Data.gameObject.AddComponent<Rigidbody>(); body.mass = .125f; body.useGravity = false;
            }

            internal void Loose(int slot)
            {
                Base.Loose(slot); Base.Data.gameObject.tag = "PART";
                var body = Base.Data.GetComponent<Rigidbody>(); if (body == null) throw new InvalidOperationException("Previous cycle lost its body.");
                body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; body.useGravity = false;
                Base.Data.FsmVariables.FindFsmFloat("Wear").Value = 93;
                NativeBagPartChecks.Fire(Base.Data, "Idle"); NativeBagPartChecks.Fire(Base.Installer, "State 1");
                Call(Base.Sync, "TryScanNativePart", body);
                var item = ((IDictionary)Get(Base.Sync, "_items"))[Id]; Set(item, "RemoteOwner", (byte)1);
                var control = Get(Base.Part, "ToolScrew"); if (control != null) Set(control, "NextRequestAt", 0f);
            }
            internal ReplacementPartState State() => (ReplacementPartState)Call(Base.Sync, "BuildReplacementPartState", Id)!;
            internal PartFitRequest Request(PartFitOperation operation, byte slot = 0) => new PartFitRequest { PlayerId = 1, Token = 445,
                Sequence = ++_sequence, ItemId = Id, ExpectedRevision = State().Revision, Operation = operation, SlotIndex = slot };
            internal PartFitStatus Execute(PartFitRequest request)
            { _player.Position = Base.Data.transform.position; _player.LastTransformTime = Time.unscaledTime; Call(Base.Sync, "OnHostPartFit", request, (byte)1); return Status(request); }
            internal PartFitStatus Status(PartFitRequest request) => ((PartFitLedger)Get(Base.Sync, "_partFitLedger")).Inspect(request, 1, out _)!.Status;
            internal void Process() { Call(Base.Sync, "ProcessNativeParts", _session); Call(Base.Sync, "ProcessPartFitting", _session); }
            internal string Describe() => "data=" + Base.Data.ActiveStateName + " screw=" + Base.Screw.ActiveStateName + " layer=" + Base.Data.gameObject.layer
                + " installed=" + State().Installed + " assembly=" + State().AssemblyId + " parent=" + Base.Data.transform.parent
                + " position=" + Base.Data.transform.localPosition + " trigger=" + Base.Pick.isTrigger + " pick=" + Base.Pick.enabled + " screwEnabled=" + Base.Screw.enabled;
            public void Dispose()
            {
                Call(Base.Sync, "ClearPartFitting");
                typeof(SessionManager).GetProperty("Instance", Static).GetSetMethod(true).Invoke(null, new object[] { _savedSession });
                // Removed parts are native scene roots, no longer children of Base.Root.
                if (Base.Data != null && Base.Data.transform.parent == null) UnityEngine.Object.DestroyImmediate(Base.Data.gameObject);
                Base.Dispose();
            }
        }
    }
}
