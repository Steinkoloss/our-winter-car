using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private static bool AtfProbe => Environment.GetEnvironmentVariable("WINTERMP_LOCAL2P_ATF_TEST") == "1";
        private const string AtfMountPath = "CORRIS/MotorPivot/MassCenter/Block/VINP_Gearbox";
        private const string AtfGearboxFactory = "CARPARTS/PARTSYSTEM/SPAWNERS_VIN/GearboxAutomatic136";
        private static PlayMakerFSM AtfMount => Find(AtfMountPath, "Data");
        private static PlayMakerFSM AtfCap => Find(AtfMountPath + "/OpenCap", "Screw");
        private static PlayMakerFSM AtfFill => Find(AtfMountPath + "/OpenCap/CapTrigger_ATFOil", "Trigger");
        private static object? AtfCoordinator => WorldSyncManager.Instance == null ? null : Get(WorldSyncManager.Instance, "_atf");
        private static object? AtfBinding => AtfCoordinator == null ? null : Get(AtfCoordinator, "_filler");
        private static IDictionary AtfBottles => (IDictionary)Get(Items, "_atf");
        private static uint _pinAtf;
        private static Vector3 _pinAtfPosition;
        private static Quaternion _pinAtfRotation;
        private static Vector3 _pinAtfCapOffset;
        private static object AtfProperty(object target, string name) => target.GetType().GetProperty(name, Members).GetValue(target, null);
        private static void AtfSetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Members).SetValue(target, value, null);
        private static string AtfNumber(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static bool AtfCommand(string[] args, List<string> rows)
        {
            if (!AtfProbe || !args[1].StartsWith("atf-", StringComparison.Ordinal)) return false;
            RequirePersistenceSandbox();
            switch (args[1])
            {
                case "atf-view": return true;
                case "atf-needs":
                    foreach (string name in new[] { "PlayerHunger", "PlayerFatigue", "PlayerThirst", "PlayerUrine" })
                    {
                        var need = FsmVariables.GlobalVariables.FindFsmFloat(name);
                        if (need == null) throw new InvalidOperationException("Native fixture need missing: " + name);
                        need.Value = 10;
                    }
                    rows.Add("atf-fixture|native hunger fatigue thirst urine set to 10 in disposable profile"); return true;
                case "atf-visit":
                case "atf-away":
                    if (Player == null || Player.parent != null) throw new InvalidOperationException("An unseated player is required.");
                    Player.position = AtfCap.transform.position + (args[1] == "atf-away" ? Vector3.right * 30 : new Vector3(0, -.5f, 1));
                    return true;
                case "atf-park":
                    var car = GameObject.Find("CORRIS").GetComponent<Rigidbody>();
                    car.velocity = car.angularVelocity = Vector3.zero; car.constraints = RigidbodyConstraints.FreezeAll;
                    rows.Add("atf-fixture|Corris body frozen for stationary pour geometry"); return true;
                case "atf-engine-fixture":
                    AtfHostOnly();
                    var engine = AtfMount.transform.parent.parent.GetComponent<Rigidbody>();
                    var hinge = engine == null ? null : engine.GetComponent<HingeJoint>();
                    if (engine == null || PathOf(engine.transform) != "CORRIS/MotorPivot/MassCenter"
                        || hinge == null || hinge.connectedBody != GameObject.Find("CORRIS").GetComponent<Rigidbody>())
                        throw new InvalidOperationException("Expected independent native engine body.");
                    engine.constraints = RigidbodyConstraints.FreezeAll; engine.velocity = engine.angularVelocity = Vector3.zero;
                    engine.position += Vector3.right * .12f; engine.transform.position = engine.position;
                    rows.Add("atf-fixture|incomplete host engine frozen and offset 0.12m; guest engine remains native"); return true;
                case "atf-prepare": AtfPrepare(rows); return true;
                case "atf-fit": AtfFit(rows); return true;
                case "atf-init-guest":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest initialization fixture only.");
                    // Only transient controls are awakened. The protected guest
                    // gearbox mount and saved part remain untouched.
                    AtfCap.gameObject.SetActive(true);
                    rows.Add("atf-fixture|awakened guest transient cap only"); return true;
                case "atf-turn":
                    if (args[2] != "up" && args[2] != "down") throw new InvalidOperationException("Unknown cap input.");
                    Enter(AtfCap, args[2] == "down" ? "Unscrew" : "Screw"); return true;
                case "atf-source":
                    AtfHostOnly();
                    var factory = Find("Spawner/CreateItems", "ATFOil");
                    factory.SendEvent("SPAWNITEM");
                    rows.Add("atf-fixture|native ATF factory SPAWNITEM"); return true;
                case "atf-oil":
                    AtfHostOnly();
                    float oil = float.Parse(args[2], CultureInfo.InvariantCulture);
                    if (float.IsNaN(oil) || oil < 0 || oil > 6.3f) throw new InvalidOperationException("Invalid fixture oil.");
                    AtfSetProperty(AtfBinding ?? throw new InvalidOperationException("ATF binding missing."), "OilLevel", oil);
                    rows.Add("atf-fixture|host oil set|" + AtfNumber(oil)); return true;
                case "atf-place": AtfPlace(args, rows); return true;
                case "atf-hold":
                    uint heldId = uint.Parse(args[2]); var held = AtfItem(heldId); var heldBody = (Rigidbody)Get(held, "Body");
                    var pickup = Find(Hand, "PickUp");
                    pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = heldBody.gameObject;
                    Enter(pickup, "Set pivot 2");
                    rows.Add("atf-fixture|native hand Set pivot 2 entered|" + heldId); return true;
                case "atf-drop":
                    _pinAtf = 0; Find(Hand, "PickUp").SendEvent("DROP_PART"); return true;
                case "atf-request":
                    if (SessionManager.Instance!.IsHost) throw new InvalidOperationException("Guest request fixture only.");
                    SessionManager.Instance.SendWorldMessage(new AtfRefillIntent {
                        VehicleId = StableHash.Fnv1a32("vehicle:CORRIS"), BottleId = uint.Parse(args[2]),
                        Action = byte.Parse(args[3]), Sequence = ushort.Parse(args[4]),
                        PlayerId = args.Length > 5 ? byte.Parse(args[5]) : SessionManager.Instance.LocalPlayerId }, Channel.ReliableOrdered);
                    return true;
                case "atf-capture":
                    if (AtfCoordinator == null) throw new InvalidOperationException("ATF coordinator missing.");
                    var state = Call(AtfCoordinator, "BuildFillerState") as AtfFillerState;
                    rows.Add("atf-capture|" + (state == null ? "none" : state.VehicleId + "|" + state.Revision + "|" + state.Flags
                        + "|" + AtfNumber(state.Rotation) + "|" + AtfNumber(state.OilLevel))); return true;
                case "atf-checks": AtfChecks(rows); return true;
                case "atf-source-checks": AtfSourceChecks(uint.Parse(args[2]), byte.Parse(args[3]), rows); return true;
                case "atf-save":
                    AtfHostOnly();
                    // The game's own SAVEGAME event reaches the real item and
                    // fitted-part ES2 actions in this marked disposable profile.
                    PlayMakerFSM.BroadcastEvent("SAVEGAME");
                    rows.Add("atf-native-save|SAVEGAME broadcast"); return true;
                case "atf-saved": AtfSaved(rows); return true;
                default: throw new InvalidOperationException("Unknown ATF probe command.");
            }
        }

        private static void AtfHostOnly()
        { if (!SessionManager.Instance!.IsHost) throw new InvalidOperationException("Host fixture only."); }

        private static void AtfPrepare(List<string> rows)
        {
            AtfHostOnly();
            var mount = AtfMount;
            if (mount.FsmVariables.FindFsmBool("Installed").Value)
            {
                if (mount.FsmVariables.FindFsmInt("Type").Value < 2) throw new InvalidOperationException("Fixture requires an empty or automatic gearbox mount.");
                rows.Add("atf-fixture|existing automatic gearbox retained"); return;
            }
            var factory = Find(AtfGearboxFactory, "Spawn");
            factory.SendEvent("SPAWNITEM");
            rows.Add("atf-fixture|native automatic gearbox factory SPAWNITEM; issue atf-fit after initialization");
        }

        private static void AtfFit(List<string> rows)
        {
            AtfHostOnly(); var mount = AtfMount;
            if (mount.FsmVariables.FindFsmBool("Installed").Value)
            { rows.Add("atf-fixture|existing fitted gearbox retained"); return; }
            var factory = Find(AtfGearboxFactory, "Spawn");
            var part = factory.FsmVariables.FindFsmGameObject("New").Value;
            if (part == null) throw new InvalidOperationException("Issue atf-prepare first.");
            var data = PunctureData(part);
            if (!data.Fsm.Initialized || !data.Fsm.Started || data.FsmVariables.FindFsmInt("Type").Value != 2
                || string.IsNullOrEmpty(data.FsmVariables.FindFsmString("ID").Value))
                throw new InvalidOperationException("Native automatic gearbox output is not initialized.");
            mount.FsmVariables.FindFsmGameObject("ActivePart").Value = part;
            // Bypass the build's prerequisite selection only for this disposable
            // fixture. Native Install 1/2 still attach the real part and cap.
            Enter(mount, "Install 1");
            rows.Add("atf-fixture|native Install 1 entered with factory output; assembly prerequisites bypassed");
        }

        private static object AtfItem(uint id)
        {
            var items = (IDictionary)Get(Items, "_items");
            if (!items.Contains(id)) throw new InvalidOperationException("ATF item is not tracked.");
            var item = items[id]; var body = Get(item, "Body") as Rigidbody;
            if (body == null || !AtfBottles.Contains(id)) throw new InvalidOperationException("Selected item is not an ATF bottle.");
            return item;
        }

        private static void AtfPlace(string[] args, List<string> rows)
        {
            var item = AtfItem(uint.Parse(args[2])); var body = (Rigidbody)Get(item, "Body");
            var bottle = AtfBottles[uint.Parse(args[2])];
            string pose = args[3];
            if (pose != "pour" && pose != "upright" && pose != "away" && pose != "miss") throw new InvalidOperationException("Unknown ATF placement fixture.");
            Call(Items, "ClaimItem", SessionManager.Instance!, item, body, Time.unscaledTime);
            var source = Get(bottle, "PourCollider") as CapsuleCollider;
            if (source == null) throw new InvalidOperationException("Empty ATF bottle has no pour collider.");
            var target = AtfFill.GetComponent<SphereCollider>();
            body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeAll;
            body.velocity = body.angularVelocity = Vector3.zero;
            // Stay inside the native pour range. Quaternion smoothing around
            // exactly zero can represent the same pose as 359.9 degrees briefly.
            body.rotation = body.transform.rotation = Quaternion.Euler(pose == "upright" ? 90 : 30, 0, 0);
            Vector3 center = source.transform.TransformPoint(source.center);
            Vector3 destination = target.transform.TransformPoint(target.center);
            if (pose == "away") destination += Vector3.up * 3;
            if (pose == "miss") destination += Vector3.right * .15f;
            body.position += destination - center; body.transform.position = body.position;
            if ((bool)Call(Items, "IsAtfHeldLocally", uint.Parse(args[2])))
            {
                _pinAtf = uint.Parse(args[2]); _pinAtfPosition = body.position; _pinAtfRotation = body.rotation;
                _pinAtfCapOffset = body.position - AtfCap.transform.position;
                body.isKinematic = true;
                rows.Add("atf-fixture|real native hand holds bottle; fixture follows visible cap position at fixed pouring angle|" + _pinAtf);
            }
            rows.Add("atf-fixture|claimed and placed bottle|" + args[2] + "|" + pose + "|" + Vector(body.position));
        }

        private static void TickAtfFixture()
        {
            if (!AtfProbe || _pinAtf == 0) return;
            RequirePersistenceSandbox();
            if (SessionManager.Instance == null || (SessionManager.Instance.State != SessionState.Connected && !SessionManager.Instance.IsHost)
                || !AtfBottles.Contains(_pinAtf) || !(bool)Call(Items, "IsAtfHeldLocally", _pinAtf)) { _pinAtf = 0; return; }
            var body = Get(AtfBottles[_pinAtf], "Body") as Rigidbody;
            if (body == null) { _pinAtf = 0; return; }
            _pinAtfPosition = AtfCap.transform.position + _pinAtfCapOffset;
            body.isKinematic = true; body.position = body.transform.position = _pinAtfPosition;
            body.rotation = body.transform.rotation = _pinAtfRotation; body.velocity = body.angularVelocity = Vector3.zero;
        }

        private static void AtfSnapshot(List<string> rows)
        {
            RequirePersistenceSandbox();
            if (Application.loadedLevelName != "GAME") return;
            rows.Add("atf-ready|" + typeof(PlayerSyncManager).GetProperty("IsLocalSpawnReady", Members).GetValue(PlayerSyncManager.Instance, null));
            rows.Add("atf-dead|" + typeof(PlayerSyncManager).GetProperty("IsLocalDead", Members).GetValue(PlayerSyncManager.Instance, null));
            foreach (string name in new[] { "PlayerHunger", "PlayerFatigue", "PlayerThirst", "PlayerUrine" })
            {
                var need = FsmVariables.GlobalVariables.FindFsmFloat(name);
                rows.Add("atf-need|" + name + "|" + (need == null ? "none" : AtfNumber(need.Value)));
            }
            if (Player != null) rows.Add("atf-local-pose|" + Vector(Player.position));
            var binding = AtfBinding;
            rows.Add("atf-binding|" + (binding != null) + "|" + (binding == null ? "none" : AtfProperty(binding, "Mounted").ToString()));
            var cap = AtfCap; var mount = AtfMount; var fill = AtfFill;
            foreach (DictionaryEntry pair in (IDictionary)Get(Items, "_items"))
            {
                if ((string)Get(pair.Value, "Path") != "CORRIS") continue;
                var car = Get(pair.Value, "Body") as Rigidbody;
                if (car == null) continue;
                rows.Add("atf-vehicle|" + pair.Key + "|" + Vector(car.position) + "|" + Vector(car.rotation.eulerAngles)
                    + "|" + Vector(car.transform.position) + "|" + Vector(car.transform.rotation.eulerAngles)
                    + "|" + Get(pair.Value, "LocallyOwned") + "|" + Get(pair.Value, "RemoteOwner") + "|" + car.isKinematic
                    + "|" + car.constraints + "|" + Vector(car.transform.InverseTransformPoint(cap.transform.position))
                    + "|" + Vector((Quaternion.Inverse(car.transform.rotation) * cap.transform.rotation).eulerAngles));
                for (var node = cap.transform; node != null && node != car.transform; node = node.parent)
                {
                    rows.Add("atf-cap-chain|" + node.name + "|" + Vector(node.localPosition) + "|" + Vector(node.localRotation.eulerAngles));
                    var engineBody = node.GetComponent<Rigidbody>();
                    if (engineBody != null) rows.Add("atf-cap-body|" + node.name + "|" + Vector(engineBody.position)
                        + "|" + Vector(engineBody.rotation.eulerAngles) + "|" + engineBody.isKinematic
                        + "|" + engineBody.constraints + "|" + engineBody.mass);
                }
            }
            var gauge = Find("GUI/Indicators/Fluids/ATF/bar", "Scale");
            foreach (var node in gauge.transform.parent.GetComponentsInChildren<Transform>(true))
                rows.Add("atf-gauge-child|" + node.name + "|" + node.gameObject.activeSelf + "|" + node.gameObject.activeInHierarchy);
            if (binding != null)
            {
                var nodes = (Transform[])Get(binding, "_guiSubtree"); var original = (bool[])Get(binding, "_oldGuiActive");
                for (int i = 0; i < nodes.Length; i++) rows.Add("atf-gauge-original|" + nodes[i].name + "|" + original[i]);
                rows.Add("atf-cap-original|" + Vector((Vector3)Get(binding, "_oldCapLocalPosition"))
                    + "|" + Vector(((Quaternion)Get(binding, "_oldCapLocalRotation")).eulerAngles));
            }
            rows.Add("atf-cap|" + cap.gameObject.activeSelf + "|" + cap.gameObject.activeInHierarchy + "|" + cap.enabled + "|"
                + cap.Fsm.Initialized + "|" + cap.Fsm.Started + "|" + cap.ActiveStateName + "|" + cap.FsmVariables.FindFsmFloat("Rot").Value
                + "|" + cap.FsmVariables.FindFsmGameObject("CapMesh").Value.activeSelf + "|" + fill.gameObject.activeSelf + "|" + Vector(cap.transform.position));
            var part = mount.FsmVariables.FindFsmGameObject("ActivePart").Value;
            PlayMakerFSM? saved = part == null ? null : PunctureData(part);
            rows.Add("atf-oil|" + mount.FsmVariables.FindFsmFloat("OilLevel").Value + "|" + mount.FsmVariables.FindFsmFloat("OilMax").Value
                + "|" + (saved == null ? "none" : AtfNumber(saved.FsmVariables.FindFsmFloat("OilLevel").Value))
                + "|" + (saved == null ? "none" : saved.FsmVariables.FindFsmString("ID").Value)
                + "|" + mount.FsmVariables.FindFsmBool("Installed").Value + "|" + mount.FsmVariables.FindFsmInt("Type").Value
                + "|" + mount.enabled + "|" + mount.ActiveStateName);
            rows.Add("atf-gauge|" + gauge.FsmVariables.FindFsmFloat("Fluid").Value + "|" + gauge.FsmVariables.FindFsmFloat("Max").Value
                + "|" + gauge.FsmVariables.FindFsmFloat("Scale").Value + "|" + gauge.transform.localScale.x + "|" + gauge.gameObject.activeInHierarchy);
            rows.Add("atf-fill|" + fill.ActiveStateName + "|" + fill.FsmVariables.FindFsmFloat("OilLevel").Value
                + "|" + fill.FsmVariables.FindFsmBool("Pouring").Value + "|" + fill.enabled);
            foreach (DictionaryEntry pair in AtfBottles)
            {
                var body = Get(pair.Value, "Body") as Rigidbody;
                if (body == null) continue;
                var data = Get(pair.Value, "Data") as PlayMakerFSM; var use = (PlayMakerFSM)Get(pair.Value, "Use");
                var item = ((IDictionary)Get(Items, "_items"))[pair.Key];
                rows.Add("atf-bottle|" + pair.Key + "|" + Get(pair.Value, "NativeId") + "|" + body.name
                    + "|" + AtfProperty(pair.Value, "Fluid") + "|" + use.FsmVariables.FindFsmFloat("Fluid").Value
                    + "|" + AtfProperty(pair.Value, "Empty") + "|" + Vector(body.position) + "|" + Vector(body.rotation.eulerAngles)
                    + "|" + (item == null ? "none" : Get(item, "LocallyOwned").ToString()) + "|" + (item == null ? "none" : Get(item, "RemoteOwner").ToString())
                    + "|" + AtfProperty(pair.Value, "IsPouring") + "|" + (data == null ? "none" : data.ActiveStateName)
                    + "|" + (data == null ? "none" : data.enabled.ToString()) + "|" + use.ActiveStateName
                    + "|" + Call(Items, "IsAtfHeldLocally", (uint)pair.Key));
            }
            var pickup = Find(Hand, "PickUp");
            foreach (var variable in pickup.FsmVariables.ObjectVariables)
                if (variable.Value is Joint joint) rows.Add("atf-hand|" + pickup.ActiveStateName + "|" + variable.Name + "|"
                    + Identity(pickup.FsmVariables.FindFsmGameObject("PickedObject").Value) + "|" + Identity(joint.connectedBody == null ? null : joint.connectedBody.gameObject));
            foreach (DictionaryEntry pair in Bags)
            {
                var use = Get(pair.Value, "Use") as PlayMakerFSM;
                string remaining = SessionManager.Instance!.IsHost ? Call(Items, "ReadBagRemaining", pair.Value).ToString() : "guest";
                if (!SessionManager.Instance.IsHost && ((IDictionary)Get(Items, "_bagStates")).Contains(pair.Key))
                    remaining = Get(((IDictionary)Get(Items, "_bagStates"))[pair.Key], "Remaining").ToString();
                rows.Add("atf-bag|" + pair.Key + "|" + remaining + "|" + (use == null ? "none" : use.ActiveStateName)
                    + "|retired=" + Call(Get(Items, "_spawnLifecycle"), "IsRetired", (uint)pair.Key));
            }
            foreach (var peer in SessionManager.Instance!.Players) rows.Add("atf-peer|" + peer.PlayerId + "|" + Vector(peer.Position) + "|" + (Time.unscaledTime - peer.LastTransformTime));
        }

        private static void AtfSaved(List<string> rows)
        {
            var part = AtfMount.FsmVariables.FindFsmGameObject("ActivePart").Value;
            if (part != null)
            {
                var data = PunctureData(part); string tag = data.FsmVariables.FindFsmString("UTOil").Value;
                string target = Path.Combine(Application.persistentDataPath, FsmVariables.GlobalVariables.FindFsmString("SaveCarparts").Value) + "?tag=" + tag;
                rows.Add("atf-saved-oil|" + tag + "|" + ES2.Exists(target) + "|" + (ES2.Exists(target) ? AtfNumber(ES2.Load<float>(target)) : "none"));
            }
            foreach (DictionaryEntry pair in AtfBottles)
            {
                var body = Get(pair.Value, "Body") as Rigidbody;
                if (body == null) continue;
                var use = (PlayMakerFSM)Get(pair.Value, "Use"); string tag = use.FsmVariables.FindFsmString("UniqueTagFluid").Value;
                string target = Path.Combine(Application.persistentDataPath, FsmVariables.GlobalVariables.FindFsmString("SaveItems").Value) + "?tag=" + tag;
                rows.Add("atf-saved-bottle|" + pair.Key + "|" + tag + "|" + ES2.Exists(target) + "|" + (ES2.Exists(target) ? AtfNumber(ES2.Load<float>(target)) : "none"));
            }
        }

        private static void AtfSourceChecks(uint id, byte actor, List<string> rows)
        {
            AtfHostOnly();
            var item = AtfItem(id); var body = (Rigidbody)Get(item, "Body");
            var source = Get(AtfBottles[id], "PourCollider") as CapsuleCollider;
            if (source == null) throw new InvalidOperationException("Live source geometry required.");
            var positionField = item.GetType().GetField("TargetPosition", Members);
            var rotationField = item.GetType().GetField("TargetRotation", Members);
            var oldPosition = (Vector3)positionField.GetValue(item);
            var oldRotation = (Quaternion)rotationField.GetValue(item);
            var oldBodyPosition = body.position; var oldBodyRotation = body.rotation;
            var rotation = Quaternion.Euler(30, 0, 0);
            Vector3 center = Quaternion.Inverse(body.transform.rotation) * (source.transform.TransformPoint(source.center) - body.transform.position);
            Vector3 position = AtfFill.transform.position - rotation * center;
            bool CanPour() => (bool)Call(AtfCoordinator!, "CanGuestPour", SessionManager.Instance!, actor, id);
            try
            {
                positionField.SetValue(item, position); rotationField.SetValue(item, rotation);
                body.position = body.transform.position = position + Vector3.right * 4;
                if (!CanPour()) throw new InvalidOperationException("Latest overlapping pose was rejected because displayed bottle lagged.");
                rows.Add("atf-source-check|latest overlapping owner pose accepted despite distant display pose");
                body.position = body.transform.position = position; body.rotation = body.transform.rotation = rotation;
                positionField.SetValue(item, position + Vector3.right);
                if (CanPour()) throw new InvalidOperationException("Withdrawn latest pose accepted from overlapping display pose.");
                rows.Add("atf-source-check|latest withdrawn owner pose rejected despite overlapping display pose");
                positionField.SetValue(item, position); rotationField.SetValue(item, Quaternion.Euler(90, 0, 0));
                if (CanPour()) throw new InvalidOperationException("Upright latest pose accepted from tilted display pose.");
                rows.Add("atf-source-check|latest upright owner pose rejected despite tilted display pose");
            }
            finally
            {
                positionField.SetValue(item, oldPosition); rotationField.SetValue(item, oldRotation);
                body.position = body.transform.position = oldBodyPosition; body.rotation = body.transform.rotation = oldBodyRotation;
            }
        }

        private static void AtfChecks(List<string> rows)
        {
            AtfHostOnly(); var binding = AtfBinding ?? throw new InvalidOperationException("ATF binding missing.");
            void Check(string label, Action action)
            {
                bool refused = false;
                try { action(); } catch (System.Reflection.TargetInvocationException error) { refused = error.InnerException is InvalidOperationException; }
                if (!refused) throw new InvalidOperationException("Changed native ATF binding was accepted: " + label);
                rows.Add("atf-check|" + label);
            }
            Call(binding, "ValidateCap"); rows.Add("atf-check|native cap validated");
            var step = AtfCap.FsmVariables.FindFsmFloat("ScrewAmount"); float oldStep = step.Value;
            try { step.Value = 34; Check("changed step rejected", () => Call(binding, "ValidateCap")); } finally { step.Value = oldStep; }
            var actions = (FsmStateAction[])Get(binding, "_upActions"); bool enabled = actions[0].Enabled;
            try { actions[0].Enabled = false; Check("disabled native step rejected", () => Call(binding, "ValidateCap")); } finally { actions[0].Enabled = enabled; }
            var rotation = AtfCap.FsmVariables.FindFsmFloat("Rot"); float oldRotation = rotation.Value;
            try { rotation.Value = 360; Check("invalid rotation rejected", () => Call(binding, "ValidateCap")); } finally { rotation.Value = oldRotation; }
            if (!(bool)Call(binding, "ValidateMounted")) throw new InvalidOperationException("Mounted native automatic expected.");
            var active = AtfMount.FsmVariables.FindFsmGameObject("ActivePart"); var original = active.Value;
            try
            {
                active.Value = null;
                if ((bool)Call(binding, "ValidateMounted")) throw new InvalidOperationException("Missing mounted part accepted.");
                rows.Add("atf-check|missing mounted part rejected");
            }
            finally { active.Value = original; }
            var data = PunctureData(original); var assembly = data.FsmVariables.FindFsmInt("AssemblyID"); int oldAssembly = assembly.Value;
            try
            {
                assembly.Value = 0;
                if ((bool)Call(binding, "ValidateMounted")) throw new InvalidOperationException("Unfitted automatic accepted.");
                rows.Add("atf-check|unfitted part rejected");
            }
            finally { assembly.Value = oldAssembly; }
            foreach (var slot in (IList)Get(binding, "_suppressed"))
            {
                var action = (FsmStateAction)Get(slot, "Action"); var state = (FsmState)Get(slot, "State");
                action.Enabled = true; Call(binding, "Tick");
                if (action.Enabled || state.ActiveActions.Contains(action)) throw new InvalidOperationException("Native transfer callback escaped suppression.");
                rows.Add("atf-check|transfer callback remains suppressed|" + state.Name + "|" + Get(slot, "Index"));
            }
        }
    }
}
