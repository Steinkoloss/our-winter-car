using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;

namespace WinterMP.GuestSaveProbe
{
    // Developer-only native interaction driver. Both opt-in environment and a
    // sandbox marker are required; it never ships or runs in a normal session.
    internal sealed partial class LiveBagProbe
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string Cash = "PERAPORTTI/ActiveFunctions/Store/Cashier/StoreCashRegister/CashRegisterLogic";
        private const string Hand = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand";
        private static readonly Type Hook = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmHook", true);
        private string _output = string.Empty, _role = string.Empty;
        private float _gameAt = -1, _nextPoll;
        private int _sequence;
        private bool _headSavePrepared;

        internal void Start()
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-live-bag-sandbox.txt")))
                throw new InvalidOperationException("Bag gameplay test requires an explicitly marked isolated copy.");
            _output = Path.Combine(BepInEx.Paths.GameRootPath, "live-bag");
            Directory.CreateDirectory(_output);
            _role = Environment.GetEnvironmentVariable("WINTERMP_LOG_ROLE") == "guest" ? "guest" : "host";
            StartPersistence();
        }

        internal void Tick()
        {
            TickStability();
            if ((!Persistence && Application.loadedLevelName != "GAME") || SessionManager.Instance == null) return;
            if (_gameAt < 0)
            {
                if (Application.loadedLevelName != "GAME") return;
                _gameAt = Time.unscaledTime;
            }
            TickHeadFixture();
            TickAtfFixture();
            TickOilRefillFixture();
            TickWiringFixture();
            TickTaxiFixture();
            TickHouseholdFuses();
            TickTrailerFixture();
            if (Time.unscaledTime < _gameAt + 15 || Time.unscaledTime < _nextPoll) return;
            if (!_headSavePrepared)
            {
                _headSavePrepared = true;
                // ES2's settings prefab is unavailable during plugin Awake on SplashScreen.
                PrepareHeadSaveFixture();
            }
            _nextPoll = Time.unscaledTime + .1f;
            string command = Path.Combine(_output, _role + "-command.txt");
            if (!File.Exists(command)) return;
            var args = File.ReadAllText(command).TrimEnd('\r', '\n').Split('\t');
            int sequence = int.Parse(args[0], CultureInfo.InvariantCulture);
            if (sequence <= _sequence) return;
            _sequence = sequence;
            var rows = new List<string>();
            try
            {
                Execute(args, rows);
                Snapshot(rows);
                rows.Insert(0, "OK|" + sequence);
            }
            catch (Exception error) { rows.Insert(0, "FAIL|" + sequence); rows.Add(error.ToString()); }
            File.WriteAllLines(Path.Combine(_output, _role + "-" + sequence + ".txt"), rows.ToArray());
        }

        private static object Items => Get(WorldSyncManager.Instance ?? throw new InvalidOperationException("World missing."), "_items");
        private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
        private static IDictionary Bags => (IDictionary)Get(Items, "_bags");
        private static Transform? Player => typeof(WorldSyncManager).GetProperty("LocalPlayer", Members).GetValue(WorldSyncManager.Instance, null) as Transform;

        private static PlayMakerFSM Find(string path, string name)
        {
            PlayMakerFSM? found = null;
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = obj as PlayMakerFSM;
                if (fsm == null) continue;
                if (fsm.FsmName != name || PathOf(fsm.transform) != path) continue;
                if (found != null) throw new InvalidOperationException("Ambiguous test target " + path);
                found = fsm;
            }
            return found ?? throw new InvalidOperationException("Missing test target " + path + "::" + name);
        }

        private static string PathOf(Transform node)
        {
            string path = node.name;
            while (node.parent != null) { node = node.parent; path = node.name + "/" + path; }
            return path;
        }

        private static void Enter(PlayMakerFSM fsm, string state)
        {
            if (!fsm.enabled || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Initialized || !fsm.Fsm.Started)
                throw new InvalidOperationException("Native target is not active and started: " + fsm.FsmName);
            if (!(bool)Hook.GetMethod("EnsureRemoteEntry", Members).Invoke(null, new object[] { fsm, state }))
                throw new InvalidOperationException("Native target state missing: " + state);
            Hook.GetMethod("FireRemoteEntry", Members).Invoke(null, new object[] { fsm, state });
        }

        private static object Bag(string id) => Bags[uint.Parse(id, CultureInfo.InvariantCulture)]
            ?? throw new InvalidOperationException("Bag is not registered.");

        private static void Execute(string[] args, List<string> rows)
        {
            if (AdvertPhoneCommand(args, rows)) return;
            if (OilRefillCommand(args, rows)) return;
            if (MotorOilCommand(args, rows)) return;
            if (AdvertCommand(args, rows)) return;
            if (TrainCommand(args, rows)) return;
            if (CoffeeCommand(args, rows)) return;
            if (SausageCommand(args, rows)) return;
            if (TrailerCommand(args, rows)) return;
            if (HouseholdFuseCommand(args, rows)) return;
            if (TaxiCommand(args, rows)) return;
            if (BulbCommand(args, rows)) return;
            if (SupplyCommand(args, rows)) return;
            if (WiringCommand(args, rows)) return;
            if (FleaCommand(args, rows)) return;
            if (AtfCommand(args, rows)) return;
            if (CookingCommand(args, rows)) return;
            if (StabilityCommand(args, rows)) return;
            if (PunctureCommand(args)) return;
            if (WoodDeliveryCommand(args)) return;
            if (HockeyCommand(args)) return;
            if (PermadeathCommand(args, rows)) return;
            if (UtilityCommand(args, rows)) return;
            if (MeatCommand(args, rows)) return;
            if (HeadSaveCommand(args, rows)) return;
            if (HeadFitCommand(args, rows)) return;
            if (MilkCommand(args, rows)) return;
            if (FirewoodCommand(args, rows)) return;
            if (ValveCommand(args, rows)) return;
            if (PersistenceCommand(args, rows)) return;
            switch (args[1])
            {
                case "snapshot": return;
                case "describe": Describe(Find(args[2], args[3]), rows); return;
                case "shop":
                    var cash = Find(Cash, "Data");
                    var spawn = cash.FsmVariables.FindFsmGameObject("SpawnPoint")?.Value;
                    if (spawn == null) throw new InvalidOperationException("Shop spawn point missing.");
                    var player = Player;
                    if (player == null) throw new InvalidOperationException("Player missing.");
                    player.position = spawn.transform.position + new Vector3(0, 0, 2);
                    return;
                case "enter":
                    if (!args[2].StartsWith("PERAPORTTI/", StringComparison.Ordinal)
                        && !args[2].StartsWith("Spawner/CreateBag", StringComparison.Ordinal))
                        throw new InvalidOperationException("Entry outside shopping test scope.");
                    Enter(Find(args[2], args[3]), args[4]); return;
                case "pickup":
                    var pickup = Find(Hand, "PickUp");
                    pickup.FsmVariables.FindFsmGameObject("PickedObject").Value = ((Rigidbody)Get(Bag(args[2]), "Body")).gameObject;
                    Enter(pickup, "Set pivot 2"); return;
                case "drop": Find(Hand, "PickUp").SendEvent("DROP_PART"); return;
                case "open": Enter((PlayMakerFSM)Get(Bag(args[2]), "Use"), args[3] == "all" ? "Spawn all" : "Spawn one"); return;
                default: throw new InvalidOperationException("Unknown shopping test command.");
            }
        }

        private static void Snapshot(List<string> rows)
        {
            var session = SessionManager.Instance!;
            rows.Add("session|" + session.State + "|" + session.PlayerCount + "|" + session.LocalPlayerId);
            PersistenceSnapshot(rows);
            if (AdvertPhoneProbe) { AdvertPhoneSnapshot(rows); return; }
            if (MotorOilProbe) { MotorOilSnapshot(rows); return; }
            if (AdvertProbe) { AdvertSnapshot(rows); return; }
            if (TrainProbe) { TrainSnapshot(rows); return; }
            if (CoffeeProbe) { CoffeeSnapshot(rows); return; }
            if (SausageProbe) { SausageSnapshot(rows); return; }
            if (TrailerProbe) { TrailerSnapshot(rows); return; }
            if (HouseholdFuseProbe) { HouseholdFuseSnapshot(rows); SupplySnapshot(rows); return; }
            if (SupplyProbe) { SupplySnapshot(rows); BulbSnapshot(rows); return; }
            if (TaxiProbe) { TaxiSnapshot(rows); return; }
            if (WiringProbe) { WiringSnapshot(rows); return; }
            if (FleaProbe) { FleaSnapshot(rows); return; }
            if (AtfProbe) { AtfSnapshot(rows); return; }
            if (CookingProbe) { CookingSnapshot(rows); return; }
            if (StabilityProbe) { StabilitySnapshot(rows); return; }
            if (PunctureProbe) { PunctureSnapshot(rows); return; }
            if (WoodDeliveryProbe) { WoodDeliverySnapshot(rows); return; }
            if (HockeyProbe) { HockeySnapshot(rows); return; }
            PermadeathSnapshot(rows);
            if (PermadeathProbe) return;
            if (Application.loadedLevelName != "GAME") return;
            var player = Player;
            rows.Add("player|" + (player != null ? Vector(player.position) : "missing"));
            var pickup = Find(Hand, "PickUp");
            var picked = pickup.FsmVariables.FindFsmGameObject("PickedObject")?.Value;
            var joint = pickup.GetComponent<FixedJoint>();
            rows.Add("hand|" + pickup.ActiveStateName + "|" + Identity(picked) + "|"
                + (joint != null && joint.connectedBody != null ? Identity(joint.connectedBody.gameObject) : "none"));
            foreach (DictionaryEntry entry in (IDictionary)Get(Items, "_bagFactories"))
            {
                var fsm = (PlayMakerFSM)Get(entry.Value, "Fsm");
                var contents = Get(entry.Value, "Contents") as PlayMakerFSM;
                rows.Add("factory|" + entry.Key + "|" + Get(entry.Value, "Prefix") + "|" + Get(entry.Value, "Failed")
                    + "|" + fsm.ActiveStateName + "|" + (contents != null ? contents.ActiveStateName : "missing"));
            }
            foreach (DictionaryEntry entry in Bags)
            {
                var bag = entry.Value; var body = Get(bag, "Body") as Rigidbody; var use = Get(bag, "Use") as PlayMakerFSM;
                string remaining = "missing";
                if (use != null && session.IsHost) remaining = Call(Items, "ReadBagRemaining", bag).ToString();
                if (!session.IsHost)
                {
                    var states = (IDictionary)Get(Items, "_bagStates");
                    if (states.Contains(entry.Key)) remaining = Get(states[entry.Key], "Remaining").ToString();
                }
                rows.Add("bag|" + entry.Key + "|" + Get(bag, "NativeId") + "|" + Get(bag, "Replica") + "|" + remaining
                    + "|" + Identity(body != null ? body.gameObject : null) + "|" + (use != null ? use.ActiveStateName : "missing"));
            }
            foreach (DictionaryEntry entry in (IDictionary)Get(Items, "_items"))
            {
                var item = entry.Value; var body = Get(item, "Body") as Rigidbody;
                if (body == null) continue;
                rows.Add("item|" + entry.Key + "|" + Identity(body.gameObject) + "|" + Get(item, "RemoteOwner")
                    + "|" + Get(item, "LocallyOwned") + "|" + body.gameObject.activeInHierarchy + "|" + Vector(body.position));
            }
            var opening = Get(Items, "_bagOpening");
            rows.Add("opening|" + (opening == null ? "none" : Get(Get(opening, "Bag"), "Id").ToString()));
            var pending = Get(Items, "_pendingBagRequest");
            rows.Add("request|" + (pending == null ? "none" : Get(pending, "ItemId").ToString()));
            var cash = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            rows.Add("cash|" + (cash != null ? cash.Value.ToString("R", CultureInfo.InvariantCulture) : "missing"));
        }

        private static void Describe(PlayMakerFSM fsm, List<string> rows)
        {
            rows.Add("fsm|" + PathOf(fsm.transform) + "|" + fsm.FsmName + "|" + fsm.ActiveStateName
                + "|" + fsm.enabled + "|" + fsm.gameObject.activeInHierarchy + "|" + fsm.Fsm.Initialized + "|" + fsm.Fsm.Started);
            foreach (var variable in fsm.FsmVariables.GameObjectVariables) rows.Add("object|" + variable.Name + "|" + Identity(variable.Value));
            foreach (var variable in fsm.FsmVariables.FloatVariables) rows.Add("float|" + variable.Name + "|" + variable.Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (var variable in fsm.FsmVariables.IntVariables) rows.Add("int|" + variable.Name + "|" + variable.Value);
            foreach (var variable in fsm.FsmVariables.StringVariables) rows.Add("string|" + variable.Name + "|" + variable.Value);
            foreach (var global in fsm.Fsm.GlobalTransitions) rows.Add("global|" + global.EventName + "|" + global.ToState);
            foreach (var state in fsm.Fsm.States)
            {
                rows.Add("state|" + state.Name);
                foreach (var action in state.Actions)
                {
                    var fields = new List<string>();
                    foreach (var field in action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var value = field.GetValue(action);
                        if (value is FsmGameObject go) fields.Add(field.Name + "=" + go.Name + ":" + Identity(go.Value));
                        else if (value is NamedVariable variable) fields.Add(field.Name + "=" + variable.Name + ":" + variable.GetType().GetProperty("Value")?.GetValue(variable, null));
                        else if (value is FsmEvent evt) fields.Add(field.Name + "=" + evt.Name);
                        else if (value is FsmOwnerDefault owner) fields.Add(field.Name + "=" + owner.OwnerOption + ":" + owner.GameObject.Name + ":" + Identity(owner.GameObject.Value));
                        else if (value is FsmEventTarget target) fields.Add(field.Name + "=" + target.target + ":" + target.gameObject.OwnerOption
                            + ":" + target.gameObject.GameObject.Name + ":" + target.fsmName.Value + ":children=" + target.sendToChildren.Value);
                        else if (value is bool flag) fields.Add(field.Name + "=" + flag);
                    }
                    rows.Add("action|" + action.GetType().Name + "|" + string.Join(";", fields.ToArray()));
                }
                foreach (var transition in state.Transitions) rows.Add("transition|" + transition.EventName + "|" + transition.ToState);
            }
        }

        private static string Vector(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + ","
            + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);
        private static string Identity(GameObject? obj) => obj == null ? "none" : obj.GetInstanceID() + ":" + PathOf(obj.transform);
    }
}
