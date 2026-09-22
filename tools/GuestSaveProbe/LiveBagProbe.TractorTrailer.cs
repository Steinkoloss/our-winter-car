using System;
using System.Collections.Generic;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal sealed partial class LiveBagProbe
    {
        private static bool TrailerProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_TRAILER_TEST") == "1";
        private static object TrailerSync => Get(WorldSyncManager.Instance!, "_trailer");
        private static Rigidbody Tractor => Find("KEKMET(350-400psi)/Trailer/Hook", "Distance").transform.root.GetComponent<Rigidbody>();
        private static Transform? _trailerPlayerParent;
        private static float _trailerNeedsAt;
        private static void TickTrailerFixture()
        {
            if (!TrailerProbe || Application.loadedLevelName != "GAME" || Time.unscaledTime < _trailerNeedsAt) return;
            RequirePersistenceSandbox(); _trailerNeedsAt = Time.unscaledTime + 5;
            foreach (string name in new[] { "PlayerHunger", "PlayerThirst", "PlayerFatigue", "PlayerStress", "PlayerUrine", "PlayerTemp" })
            { var v = FsmVariables.GlobalVariables.FindFsmFloat(name); if (v != null) v.Value = name == "PlayerTemp" ? 50 : 0; }
        }

        private static bool TrailerCommand(string[] args, List<string> rows)
        {
            if (!TrailerProbe || !args[1].StartsWith("trailer-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            var sync = TrailerSync; var bodies = (Rigidbody[]?)Get(sync, "_bodies");
            if (args[1] == "trailer-view") return true;
            if (bodies == null) throw new InvalidOperationException("Trailer adapter not ready.");
            switch (args[1])
            {
                case "trailer-near": Player!.position = Find("KEKMET(350-400psi)/Trailer/Remove", "Use").transform.position + Vector3.up; return true;
                case "trailer-far": Player!.position = Tractor.position + Vector3.up + Vector3.right * 30; return true;
                case "trailer-align":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var car = Tractor; var trailerRoot = bodies[0].transform;
                    var carP = car.position; var carQ = car.rotation; var newQ = trailerRoot.rotation;
                    var hitch = Find("KEKMET(350-400psi)/Trailer/Hook", "Distance");
                    var newP = trailerRoot.Find("HookTarget").position - newQ * car.transform.InverseTransformPoint(hitch.transform.position);
                    var carBodies = car.GetComponentsInChildren<Rigidbody>(true);
                    var carPositions = new Vector3[carBodies.Length]; var carRotations = new Quaternion[carBodies.Length];
                    for (int i = 0; i < carBodies.Length; i++) { carPositions[i] = Quaternion.Inverse(carQ) * (carBodies[i].position - carP); carRotations[i] = Quaternion.Inverse(carQ) * carBodies[i].rotation; }
                    for (int i = 0; i < carBodies.Length; i++) { carBodies[i].transform.position = newP + newQ * carPositions[i]; carBodies[i].transform.rotation = newQ * carRotations[i]; carBodies[i].velocity = carBodies[i].angularVelocity = Vector3.zero; }
                    Enter(hitch, "Feel trailer"); return true;
                case "trailer-away":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only.");
                    var root = bodies[0].transform; var oldP = root.position; var oldQ = root.rotation;
                    var target = root.Find("HookTarget"); var hook = Find("KEKMET(350-400psi)/Trailer/Hook", "Distance").transform;
                    var turn = Tractor.rotation;
                    var pos = hook.position - turn * root.InverseTransformPoint(target.position);
                    if (args[1] == "trailer-away") pos -= turn * Vector3.forward * 5;
                    var positions = new Vector3[3]; var rotations = new Quaternion[3];
                    for (int i = 0; i < 3; i++) { positions[i] = Quaternion.Inverse(oldQ) * (bodies[i].position - oldP); rotations[i] = Quaternion.Inverse(oldQ) * bodies[i].rotation; }
                    for (int i = 0; i < 3; i++) { bodies[i].position = pos + turn * positions[i]; bodies[i].rotation = turn * rotations[i]; bodies[i].velocity = bodies[i].angularVelocity = Vector3.zero; }
                    return true;
                case "trailer-release": Enter(Find("KEKMET(350-400psi)/Trailer/Remove", "Use"), "Close door"); return true;
                case "trailer-intent":
                    SessionManager.Instance!.SendWorldMessage(new TractorTrailerIntent { PlayerId = byte.Parse(args[2]), Revision = uint.Parse(args[3]), Sequence = uint.Parse(args[4]) }, Channel.ReliableOrdered); return true;
                case "trailer-drive":
                    _trailerPlayerParent = Player!.parent; Player.parent = Tractor.transform; Player.localPosition = new Vector3(0, 1, 0); return true;
                case "trailer-exit": Player!.parent = _trailerPlayerParent; Player.position = Tractor.position + Vector3.right * 3; return true;
                case "trailer-velocity":
                    if (!(bool)Get(sync, "_localPhysics")) throw new InvalidOperationException("Local trailer physics authority required.");
                    Tractor.velocity = Tractor.transform.forward * float.Parse(args[2], CultureInfo.InvariantCulture); return true;
                case "trailer-motion-forge":
                    var source = (TractorTrailerState)(Get(sync, "_remote") ?? Get(sync, "_current"));
                    var state = (TractorTrailerState)PacketCodec.Decode(PacketCodec.Encode(source));
                    var motion = new TractorTrailerMotion { Owner = byte.Parse(args[2]), Revision = state.Revision, Sequence = uint.Parse(args[3]), Bodies = state.Bodies };
                    motion.Bodies[0].Position.X += 100;
                    motion.Bodies[1].Position.X += 100;
                    SessionManager.Instance!.SendWorldMessage(motion, Channel.UnreliableSequenced); return true;
                default: throw new InvalidOperationException("Unknown trailer fixture.");
            }
        }

        private static void TrailerSnapshot(List<string> rows)
        {
            var sync = TrailerSync; var state = (TractorTrailerState?)(Get(sync, "_current") ?? Get(sync, "_remote"));
            rows.Add("trailer|" + Get(sync, "_failed") + "|" + Get(sync, "_localPhysics") + "|" + (state == null ? "none" : state.Revision + "|" + state.Owner + "|" + state.Attached));
            rows.Add("trailer-requests|" + Get(sync, "_intentSequence") + "|" + ((System.Collections.IDictionary)Get(sync, "_requests")).Count);
            if (Player != null) rows.Add("trailer-player|" + Vector(Player.position) + "|" + PathOf(Player));
            foreach (var peer in SessionManager.Instance!.Players) rows.Add("trailer-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + peer.IsDead + "|" + (Time.unscaledTime - peer.LastTransformTime));
            var hook = Find("KEKMET(350-400psi)/Trailer/Hook", "Distance");
            var remove = Find("KEKMET(350-400psi)/Trailer/Remove", "Use");
            rows.Add("trailer-native|" + hook.FsmVariables.FindFsmBool("TrailerAttached").Value + "|" + hook.enabled + "|" + hook.ActiveStateName + "|" + remove.gameObject.activeSelf + "|" + remove.ActiveStateName);
            var root = Find("FLATBED", "Detach").transform;
            foreach (var b in root.GetComponentsInChildren<Rigidbody>(true))
                rows.Add("trailer-body|" + PathOf(b.transform) + "|" + b.isKinematic + "|" + Vector(b.position) + "|" + Vector(b.velocity));
            foreach (var j in root.GetComponentsInChildren<Joint>(true))
                rows.Add("trailer-joint|" + PathOf(j.transform) + "|" + (j.connectedBody == null ? "world" : PathOf(j.connectedBody.transform)) + "|" + JointSeparation(j).ToString("R", CultureInfo.InvariantCulture));
            rows.Add("trailer-tractor|" + Vector(Tractor.position) + "|" + Tractor.isKinematic + "|" + Vector(Tractor.velocity));
            var item = Get(sync, "_tractor");
            if (item != null) rows.Add("trailer-ownership|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner") + "|" + Get(item, "RemoteIsDriver"));
        }
    }
}
