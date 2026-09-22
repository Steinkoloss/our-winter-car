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
        private static bool TrainProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_TRAIN_TEST") == "1";
        private static int _trainContacts;
        private static PlayMakerFSM? _trainObserved;
        private static object Train => Get(WorldSyncManager.Instance!, "_train");
        private static Rigidbody TrainBody
        {
            get
            {
                var body = Get(Train, "_body") as Rigidbody;
                if (body != null) return body;
                foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(Rigidbody)))
                { var b = obj as Rigidbody; if (b != null && b.name == "TRAIN" && PathOf(b.transform).StartsWith("TRAIN/", StringComparison.Ordinal)) return b; }
                throw new InvalidOperationException("Native train missing.");
            }
        }
        private static GameObject? TrainLocalPlayer()
        {
            foreach (var state in TrainFsm("Player").FsmStates)
                if (state.Name == "Player 2") foreach (var action in state.Actions)
                    if (action.GetType().Name == "GameObjectCompare") return ((FsmGameObject)Get(action, "compareTo")).Value;
            return null;
        }
        private static PlayMakerFSM TrainFsm(string name) => Find(PathOf(TrainBody.transform), name);
        private static bool TrainCommand(string[] a, List<string> rows)
        {
            if (!TrainProbe || !a[1].StartsWith("train-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            switch (a[1])
            {
                case "train-view": return true;
                case "train-nonperma":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Disposable host setting only.");
                    PermaFlag.Value = false; RunPermaAction(true); Call(SessionManager.Instance, "SetPermanentDeathEnabled", false); return true;
                case "train-near": Player!.position = TrainBody.position + new Vector3(0, 4, 20); return true;
                case "train-stale": Train.GetType().GetField("_receivedAt", Members).SetValue(Train, Time.unscaledTime - 5); return true;
                case "train-stream":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host stream fixture only.");
                    Train.GetType().GetField("_sendAt", Members).SetValue(Train, a[2] == "off" ? Time.unscaledTime + 600 : 0f); return true;
                case "train-native":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host native lifecycle fixture only.");
                    Enter(TrainFsm(a[2]), a[3]); return true;
                case "train-volume":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host audio fixture only.");
                    TrainBody.transform.Find("Sound").GetComponent<AudioSource>().volume = float.Parse(a[2], CultureInfo.InvariantCulture); return true;
                case "train-light":
                    if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host lights fixture only.");
                    Enter(Find(PathOf(TrainBody.transform) + "/Mesh/Lights", "Lights Switch"), a[2]); return true;
                case "train-resync":
                    SessionManager.Instance!.SendWorldMessage(new WorldObjectStateRequest { NetId = (uint)Train.GetType().GetProperty("NetId", Members).GetValue(Train, null) }, Channel.ReliableOrdered); return true;
                case "train-replay":
                    var state = (TrainState)Get(Train, "_remote"); var duplicate = (TrainState)PacketCodec.Decode(PacketCodec.Encode(state));
                    duplicate.Position.X += 1000; duplicate.HornSequence += 100;
                    Call(Train, "Receive", duplicate); return true;
                case "train-impact":
                    // An actual dynamic collider hits the native train; the local-player
                    // comparison must reject this unrelated body without killing anyone.
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "Train audit collision"; cube.tag = "Player";
                    cube.transform.position = TrainBody.transform.Find("Mesh/train").TransformPoint(new Vector3(0, 0, 0));
                    cube.AddComponent<Rigidbody>().useGravity = false; UnityEngine.Object.Destroy(cube, 3); return true;
                case "train-kinematic": TrainBody.isKinematic = bool.Parse(a[2]); return true;
                case "train-hit-player":
                    if (SessionManager.Instance!.PermanentDeathEnabled) throw new InvalidOperationException("Non-permadeath fixture required.");
                    var cc = Player!.GetComponent<CharacterController>();
                    var bounds = TrainBody.transform.Find("Mesh/train").GetComponent<Collider>().bounds;
                    Player.position = bounds.center + Vector3.up * (bounds.extents.y + 2);
                    cc.Move(Vector3.down * 5); return true;
                case "train-cross-player":
                    if (SessionManager.Instance!.PermanentDeathEnabled) throw new InvalidOperationException("Non-permadeath fixture required.");
                    var collider = TrainBody.transform.Find("Mesh/train").GetComponent<BoxCollider>();
                    var target = collider.transform.TransformPoint(collider.center);
                    Player!.position = target + Vector3.up * 6;
                    Player.GetComponent<CharacterController>().Move(Vector3.down * 8); return true;
                case "train-recover":
                    if (SessionManager.Instance!.PermanentDeathEnabled) throw new InvalidOperationException("Non-permadeath fixture required.");
                    var d = Find("Systems/Death", "Activate Dead Body");
                    if (d.ActiveStateName != "Newspaper" && d.ActiveStateName != "State 2") throw new InvalidOperationException("Death screen not ready: " + d.ActiveStateName);
                    d.SendEvent("FINISHED"); return true;
                case "train-player":
                    if (SessionManager.Instance!.PermanentDeathEnabled) throw new InvalidOperationException("Non-permadeath fixture required.");
                    var playerFsm = TrainFsm("Player");
                    playerFsm.FsmVariables.FindFsmGameObject("Collider").Value = TrainLocalPlayer();
                    Enter(playerFsm, "Player 2"); return true;
                default: throw new InvalidOperationException("Unknown train fixture.");
            }
        }
        private static void TrainSnapshot(List<string> rows)
        {
            if (Application.loadedLevelName != "GAME") return;
            var body = TrainBody;
            if (_trainObserved == null)
            {
                _trainObserved = TrainFsm("Player");
                Hook.GetMethod("OnStateEnter", Members, null, new[] { typeof(PlayMakerFSM), typeof(string), typeof(Action) }, null)
                    .Invoke(null, new object[] { _trainObserved, "Player 2", (Action)(() => _trainContacts++) });
            }
            rows.Add("train-contact|" + _trainContacts + "|" + Identity(TrainFsm("Player").FsmVariables.FindFsmGameObject("Collider").Value));
            rows.Add("train-local|" + (Player == null ? "unbound" : Vector(Player.position)) + "|" + Player?.GetComponent<CharacterController>()?.enabled + "|" + Identity(TrainLocalPlayer()));
            var death = Find("Systems/Death", "Activate Dead Body");
            rows.Add("train-native-death|" + death.ActiveStateName + "|" + death.gameObject.activeSelf);
            rows.Add("train-perma|" + SessionManager.Instance!.PermanentDeathEnabled);
            foreach (var peer in SessionManager.Instance.Players) rows.Add("train-peer|" + peer.PlayerId + "|" + peer.IsDead);
            rows.Add("train|" + (Get(Train, "_body") != null) + "|" + Get(Train, "_failed") + "|" + PathOf(body.transform)
                + "|" + Vector(body.position) + "|" + body.isKinematic + "|" + body.useGravity + "|" + body.constraints + "|" + Time.unscaledTime);
            foreach (var obj in body.GetComponentsInChildren<PlayMakerFSM>(true))
                rows.Add("train-fsm|" + PathOf(obj.transform).Substring(PathOf(body.transform).Length) + "|" + obj.FsmName + "|" + obj.ActiveStateName + "|" + obj.enabled);
            int mask = 0, i = 0;
            var catalog = Get(Train, "_c");
            if (catalog == null) { rows.Add("train-unbound-restored"); return; }
            var config = (IEnumerable<string>)Get(catalog, "Colliders");
            foreach (string path in config)
            { var c = body.transform.Find(path).GetComponent<Collider>(); if (c.enabled && c.gameObject.activeInHierarchy) mask |= 1 << i; i++; }
            rows.Add("train-display|" + body.gameObject.activeSelf + "|" + body.transform.Find("Mesh").gameObject.activeSelf
                + "|" + body.transform.Find("Mesh/Lights/BeamsShort").gameObject.activeSelf + "|" + mask
                + "|" + body.transform.Find("Sound").GetComponent<AudioSource>().volume + "|" + body.transform.Find("Whistle").GetComponent<AudioSource>().isPlaying);
            var s = Get(Train, "_remote") as TrainState ?? Call(Train, "Snapshot") as TrainState;
            if (s != null) rows.Add("train-state|" + s.Sequence + "|" + s.Phase + "|" + s.HornSequence + "|" + s.Flags + "|" + s.ColliderMask
                + "|" + s.Position + "|" + Get(Train, "_presentedHorn") + "|" + (Time.unscaledTime - (float)Get(Train, "_receivedAt")));
            rows.Add("train-death|" + DeathSyncManager.Instance!.GetType().GetProperty("IsLocalDead", Members).GetValue(DeathSyncManager.Instance, null) + "|" + TrainFsm("Player").ActiveStateName);
        }
    }
}
