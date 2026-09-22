using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool StabilityProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_STABILITY_TEST") == "1";
        private float _nextStability;
        private int _stabilitySamples;
        private static ushort _stabilitySequence = 20000;

        private static bool StabilityCommand(string[] args, List<string> rows)
        {
            if (!StabilityProbe || !args[1].StartsWith("stability-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            var item = PunctureItem; var body = PunctureCar.GetComponent<Rigidbody>();
            switch (args[1])
            {
                case "stability-checks": StabilityNativeChecks(rows); return true;
                case "stability-stream":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    float distance = float.Parse(args[2], CultureInfo.InvariantCulture);
                    if (float.IsNaN(distance) || Math.Abs(distance) > 50) throw new InvalidOperationException("Invalid pose fixture.");
                    var target = body.position + Vector3.right * distance;
                    var rotation = Quaternion.Euler(0, distance * 3, 0) * body.rotation;
                    if (!(bool)Call(Items, "MoveRemoteBody", item, target, rotation, true)) throw new InvalidOperationException("Host fixture pose refused.");
                    body.velocity = body.angularVelocity = Vector3.zero; body.Sleep();
                    SessionManager.Instance.SendWorldMessage(new ItemTransform { ItemId = (uint)Get(item, "Id"), OwnerPlayerId = 0,
                        Sequence = ++_stabilitySequence, Position = new WinterMP.Net.NetVector3(target.x, target.y, target.z),
                        Rotation = new WinterMP.Net.NetQuaternion(rotation.x, rotation.y, rotation.z, rotation.w),
                        Flags = (byte)(ItemTransform.FlagVehicle | (args[3] == "final" ? ItemTransform.FlagFinal : 0)) }, WinterMP.Net.Channel.ReliableOrdered);
                    return true;
                case "stability-claim":
                    Call(Items, "ClaimItem", SessionManager.Instance!, item, body, Time.unscaledTime); return true;
                default: throw new InvalidOperationException("Unknown stability command.");
            }
        }

        private static void StabilityNativeChecks(List<string> rows)
        {
            var item = PunctureItem; var body = PunctureCar.GetComponent<Rigidbody>();
            var joint = body.GetComponent<FixedJoint>();
            if (joint == null || joint.connectedBody != null) throw new InvalidOperationException("Native world parking joint required.");
            var control = Find("CORRIS", "LOD"); var action = NativeBagPartChecks.State(control, "Remove joint").Actions[0];
            var component = (FsmString)action.GetType().GetField("component").GetValue(action);
            var position = body.position; var rotation = body.rotation;
            var target = position + new Vector3(4, 0, 3); var turn = Quaternion.Euler(0, 37, 0) * rotation;
            bool Move(Vector3 p, Quaternion q) => (bool)Call(Items, "MoveRemoteBody", item, p, q, true);
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); rows.Add("stability-check|" + name); }
            string original = component.Value; bool enabled = action.Enabled;
            try
            {
                component.Value = "HingeJoint";
                Check(!Move(target, turn) && body.GetComponent<FixedJoint>() == joint && body.position == position, "changed native release refuses pose and preserves joint");
                component.Value = original; action.Enabled = false;
                Check(!Move(target, turn) && body.GetComponent<FixedJoint>() == joint, "disabled release refuses pose");
            }
            finally { component.Value = original; action.Enabled = enabled; }
            var extra = body.gameObject.AddComponent<FixedJoint>();
            try { Check(!Move(target, turn) && body.GetComponents<FixedJoint>().Length == 2, "ambiguous world joints remain intact"); }
            finally { UnityEngine.Object.DestroyImmediate(extra); }
            var joints = body.GetComponentsInChildren<Joint>(true);
            var linked = new List<Joint>(); foreach (var child in joints) if (child.connectedBody != null) linked.Add(child);
            float force = joint.breakForce, torque = joint.breakTorque; bool automatic = joint.autoConfigureConnectedAnchor;
            var references = new List<FsmObject>();
            foreach (var fsm in body.GetComponents<PlayMakerFSM>())
                foreach (var variable in fsm.FsmVariables.ObjectVariables) if (ReferenceEquals(variable.Value, joint)) references.Add(variable);
            try
            {
                Check(Move(target, turn) && Vector3.Distance(body.position, target) < .001f && Quaternion.Angle(body.rotation, turn) < .01f, "translated and rotated parked pose is applied");
                var replacement = body.GetComponent<FixedJoint>();
                Check(joint == null && replacement != null && replacement.connectedBody == null && replacement.breakForce == force
                    && replacement.breakTorque == torque && replacement.autoConfigureConnectedAnchor == automatic, "parking lock recreated with native settings");
                foreach (var variable in references) Check(ReferenceEquals(variable.Value, replacement), "native joint variable follows replacement");
                foreach (var child in linked) Check(child != null && child.connectedBody != null, "connected part joint survives relocation");
            }
            finally { Move(position, rotation); body.velocity = body.angularVelocity = Vector3.zero; body.Sleep(); }
        }

        private void TickStability()
        {
            if (!StabilityProbe || Application.loadedLevelName != "GAME" || Time.unscaledTime < _nextStability || _stabilitySamples >= 1800) return;
            RequirePersistenceSandbox();
            _nextStability = Time.unscaledTime + .1f;
            var car = GameObject.Find("CORRIS"); var body = car?.GetComponent<Rigidbody>();
            if (body == null) return;
            _stabilitySamples++;
            float maximum = 0; string worst = "none";
            foreach (var joint in car!.GetComponentsInChildren<Joint>(true))
            {
                if (joint.connectedBody == null) continue;
                float separation = JointSeparation(joint);
                if (separation > maximum) { maximum = separation; worst = PathOf(joint.transform); }
            }
            File.AppendAllText(Path.Combine(_output, _role + "-stability-trace.txt"), Time.unscaledTime.ToString("R", CultureInfo.InvariantCulture)
                + "|" + Vector(body.position) + "|" + Vector(body.velocity) + "|" + Vector(body.angularVelocity) + "|" + body.isKinematic
                + "|" + body.IsSleeping() + "|" + maximum.ToString("R", CultureInfo.InvariantCulture) + "|" + worst + "\n");
        }

        private static float JointSeparation(Joint joint) => Vector3.Distance(joint.transform.TransformPoint(joint.anchor),
            joint.connectedBody == null ? joint.connectedAnchor : joint.connectedBody.transform.TransformPoint(joint.connectedAnchor));

        private static void StabilitySnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            if (Application.loadedLevelName != "GAME") return;
            var car = PunctureCar; var root = car.GetComponent<Rigidbody>();
            var item = PunctureItem;
            rows.Add("stability-car|" + Vector(root.position) + "|" + Vector(root.velocity) + "|" + Vector(root.angularVelocity)
                + "|" + root.isKinematic + "|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner") + "|" + root.constraints);
            foreach (var parking in root.GetComponents<FixedJoint>())
                if (parking.connectedBody == null) rows.Add("stability-parking|" + parking.GetInstanceID());
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
            {
                if (!body.transform.IsChildOf(car.transform) && Vector3.Distance(body.position, root.position) > 10) continue;
                rows.Add("stability-body|" + Identity(body.gameObject) + "|" + Vector(body.position) + "|" + Vector(body.transform.localPosition)
                    + "|" + Vector(body.velocity) + "|" + body.isKinematic + "|" + body.constraints + "|" + body.mass);
            }
            foreach (var joint in UnityEngine.Object.FindObjectsOfType<Joint>())
            {
                if (!joint.transform.IsChildOf(car.transform) && (joint.connectedBody == null || !joint.connectedBody.transform.IsChildOf(car.transform))) continue;
                rows.Add("stability-joint|" + PathOf(joint.transform) + "|" + joint.GetType().Name + "|"
                    + Identity(joint.connectedBody?.gameObject) + "|" + Vector(joint.anchor) + "|" + Vector(joint.connectedAnchor)
                    + "|" + JointSeparation(joint).ToString("R", CultureInfo.InvariantCulture)
                    + "|" + joint.autoConfigureConnectedAnchor + "|" + joint.breakForce + "|" + joint.breakTorque + "|" + joint.enableCollision + "|" + joint.enablePreprocessing);
            }
            foreach (var fsm in car.GetComponents<PlayMakerFSM>())
                rows.Add("stability-fsm|" + fsm.FsmName + "|" + fsm.ActiveStateName + "|" + fsm.enabled);
            rows.Add("stability-events|" + ((string)typeof(WinterMP.Core.Session.SessionManager).Assembly.GetType("WinterMP.Core.Diagnostics.SyncEventLog", true).GetMethod("GetSnapshot", Members).Invoke(null, null)).Replace("\r", "").Replace("\n", " ~ "));
        }
    }
}
