using System;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Core.UI;

namespace WinterMP.GuestSaveProbe
{
    // Isolated native driving driver; no save operations, no normal-install activation.
    internal sealed partial class LiveDriveProbe
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private string _output = string.Empty, _role = string.Empty;
        private float _readyAt = -1, _nextPoll;
        private int _sequence;
        private static float _controlsUntil;
        private static float _throttle, _brake, _steer, _handbrake, _clutch;
        private static int _gear;
        private static PlayMakerFSM? _drive;
        private StreamWriter? _trace;
        private float _traceUntil;
        private int _traceRows;
        private string _tracePhase = "start";
        private Rigidbody? _traceBody;
        private object? _traceItem, _traceEngine, _traceController;
        private PlayMakerFSM? _traceParking;
        internal void Start()
        {
            if (!File.Exists(Path.Combine(BepInEx.Paths.GameRootPath, "wintermp-live-drive-sandbox.txt")))
                throw new InvalidOperationException("Live drive probe requires a marked isolated copy.");
            _output = Path.Combine(BepInEx.Paths.GameRootPath, "live-drive"); Directory.CreateDirectory(_output);
            _role = Environment.GetEnvironmentVariable("WINTERMP_LOG_ROLE") == "guest" ? "guest" : "host";
            var axis = Assembly.Load("Assembly-CSharp").GetType("AxisCarController", true);
            new Harmony("com.ourwintercar.probe.shared-drive").Patch(axis.GetMethod("GetInput", Members),
                prefix: new HarmonyMethod(typeof(LiveDriveProbe).GetMethod("Input", BindingFlags.Static | BindingFlags.NonPublic)));
            StartRecovery();
        }
        private static bool Input(object __instance, ref float throttleInput, ref float brakeInput, ref float steerInput,
            ref float handbrakeInput, ref float clutchInput, ref bool startEngineInput, ref int targetGear)
        {
            if (Time.unscaledTime >= _controlsUntil || (__instance as Component)?.gameObject.name != CarPath
                || Drive.ActiveStateName != "Player in car") return true;
            throttleInput = _throttle; brakeInput = _brake; steerInput = _steer;
            handbrakeInput = _handbrake; clutchInput = _clutch; startEngineInput = false; targetGear = _gear;
            return false;
        }
        internal void Tick()
        {
            if ((Application.loadedLevelName != "GAME" && !RecoveryEnabled) || SessionManager.Instance == null) return;
            if (_readyAt < 0) _readyAt = Time.unscaledTime + 15;
            if (Time.unscaledTime < _readyAt || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + .1f;
            TraceMotion();
            string command = Path.Combine(_output, _role + "-command.txt");
            if (!File.Exists(command)) return;
            var args = File.ReadAllText(command).TrimEnd('\r', '\n').Split('\t');
            int sequence = int.Parse(args[0], CultureInfo.InvariantCulture);
            if (sequence <= _sequence) return;
            _sequence = sequence;
            var rows = new List<string>();
            try { Execute(args); Snapshot(rows); rows.Insert(0, "OK|" + sequence); }
            catch (Exception error) { rows.Insert(0, "FAIL|" + sequence); rows.Add(error.ToString()); }
            File.WriteAllLines(Path.Combine(_output, _role + "-" + sequence + ".txt"), rows.ToArray());
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, Members).GetValue(target);
        private static PlayMakerFSM Find(string path, string name)
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var fsm = (PlayMakerFSM)obj;
                if (fsm.FsmName == name && PathOf(fsm.transform) == path) return fsm;
            }
            throw new InvalidOperationException("Missing " + path + "::" + name);
        }
        private static string PathOf(Transform node)
        { string path = node.name; while (node.parent != null) { node = node.parent; path = node.name + "/" + path; } return path; }
        private const string CarPath = "SORBET(190-200psi)";
        private static GameObject Car => GameObject.Find(CarPath) ?? throw new InvalidOperationException("Car missing.");
        private static Transform Player => FsmVariables.GlobalVariables.FindFsmGameObject("SavePlayer")?.Value?.transform
            ?? throw new InvalidOperationException("Player missing.");
        private static PassengerController Passenger => PassengerController.Instance!;
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Members).Invoke(target, args);
        private static object Item
        {
            get
            {
                var items = (IDictionary)Get(Get(WorldSyncManager.Instance!, "_items"), "_items");
                foreach (DictionaryEntry entry in items)
                    if ((bool)Get(entry.Value, "IsVehicle") && ((Rigidbody)Get(entry.Value, "Body"))?.gameObject == Car) return entry.Value;
                throw new InvalidOperationException("Car not registered.");
            }
        }
        private static uint Id => (uint)Get(Item, "Id");
        private static object Seats => ((IDictionary)Get(Passenger, "_vehicles"))[Id]
            ?? throw new InvalidOperationException("Passenger seats not registered.");
        private static PlayMakerFSM Drive => _drive != null ? _drive : (_drive = Find(CarPath + "/Functions/PlayerTrigger/DriveTriggerX", "PlayerTrigger"));
        private static PlayMakerFSM Ignition => Find(CarPath + "/Functions/SteeringColumnPivot/SteeringColumn/IGNITIONxSorbet", "Use");
        private static void EnterState(PlayMakerFSM fsm, string state)
        {
            var hook = typeof(SessionManager).Assembly.GetType("WinterMP.Core.Sync.FsmHook", true);
            hook.GetMethod("EnsureRemoteEntry").Invoke(null, new object[] { fsm, state });
            hook.GetMethod("FireRemoteEntry").Invoke(null, new object[] { fsm, state });
        }
        private void Execute(string[] args)
        {
            var session = SessionManager.Instance!;
            if (RecoveryCommand(args)) return;
            if (args[1] == "snapshot" || args[1] == "inspect") return;
            if (args[1] == "trace-start")
            {
                StopTrace();
                _traceBody = Car.GetComponent<Rigidbody>(); _traceItem = Item;
                _traceEngine = Car.GetComponent("Drivetrain"); _traceController = Car.GetComponent("AxisCarController");
                _traceParking = Find(CarPath + "/Functions/HandBrake/LeverPivot", "Brake");
                _traceUntil = Time.unscaledTime + Number(args[2], 1, 600); _traceRows = 0; _tracePhase = "start";
                _trace = new StreamWriter(Path.Combine(_output, _role + "-motion.txt"), false);
                _trace.WriteLine("utc|time|phase|position|velocity|angular|kinematic|owned|remote|driver|player|parent|rpm|gear|throttle|brake|handbrakeInput|parking|fixtureUntil");
                return;
            }
            if (args[1] == "trace-mark") { _tracePhase = args[2]; return; }
            if (args[1] == "trace-stop") { StopTrace(); return; }
            if (args[1] == "join")
            { session.StartJoinLocal("127.0.0.1", WinterMP.Net.Transport.UdpTransport.DefaultPort); return; }
            if (args[1] == "screenshot")
            { Application.CaptureScreenshot(Path.Combine(BepInEx.Paths.GameRootPath, "live-drive/" + Environment.GetEnvironmentVariable("WINTERMP_LOG_ROLE") + "-view.png")); return; }
            if (args[1] == "parking")
            {
                if (Drive.ActiveStateName != "Player in car") throw new InvalidOperationException("Driver required.");
                EnterState(Find(CarPath + "/Functions/HandBrake/LeverPivot", "Use"), args.Length > 2 && args[2] == "on" ? "INCREASE" : "DECREASE"); return;
            }
            if (args[1] == "controls")
            {
                if (Drive.ActiveStateName != "Player in car") throw new InvalidOperationException("Driver required.");
                _throttle = Number(args[2], 0, 1); _brake = Number(args[3], 0, 1); _steer = Number(args[4], -1, 1);
                _handbrake = Number(args[5], 0, 1); _clutch = Number(args[6], 0, 1);
                _gear = (int)Number(args[7], 0, 6); _controlsUntil = Time.unscaledTime + Number(args[8], 0, 15); return;
            }
            if (args[1] == "near")
            {
                if (Player.parent != null || Passenger.IsLocalSeated) throw new InvalidOperationException("Exit before repositioning.");
                Player.position = args[2] == "driver" ? Drive.transform.position - Vector3.up * .6f
                    : Car.transform.TransformPoint(((Vector3[])Get(Seats, "SeatLocal"))[0] - Vector3.up * .4f);
                return;
            }
            if (args[1] == "driver")
            {
                if (Passenger.IsLocalSeated || !Drive.GetComponent<Collider>().enabled || Drive.ActiveStateName != "Press return")
                    throw new InvalidOperationException("Native driver entry is unavailable: " + Drive.ActiveStateName);
                Drive.SendEvent("Key DOWN"); return;
            }
            if (args[1] == "passenger")
            {
                if ((bool)Call(Passenger, "IsSeatOccupied", Id, (byte)0) || (string?)Get(Passenger, "_hint") != "ENTER - Sit down")
                    throw new InvalidOperationException("Passenger entry hint is unavailable.");
                Call(Passenger, "Enter", session, Seats, 0); return;
            }
            if (args[1] == "exit")
            {
                if (Passenger.IsLocalSeated) Call(Passenger, "Exit", session);
                else if (Drive.ActiveStateName == "Player in car") Drive.SendEvent("Key DOWN");
                else throw new InvalidOperationException("Not in a seat.");
                return;
            }
            if (args[1] == "ignition")
            {
                if (Drive.ActiveStateName != "Player in car") throw new InvalidOperationException("Driver required.");
                if (args[2] == "acc") Ignition.SendEvent("ACC");
                else if (args[2] == "start") Ignition.SendEvent("START");
                else if (args[2] == "release") Ignition.SendEvent("FINISHED");
                else if (args[2] == "off") EnterState(Ignition, "Motor OFF");
                else throw new InvalidOperationException("Unknown ignition action.");
                return;
            }
            throw new InvalidOperationException("Unknown driving command.");
        }

        private void StopTrace() { if (_trace != null) { _trace.Dispose(); _trace = null; } }

        private void TraceMotion()
        {
            if (_trace == null) return;
            if (Time.unscaledTime >= _traceUntil || _traceBody == null) { StopTrace(); return; }
            try
            {
                var body = _traceBody;
                _trace.WriteLine(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "|" + Time.unscaledTime + "|" + _tracePhase
                    + "|" + V(body.position) + "|" + V(body.velocity) + "|" + V(body.angularVelocity) + "|" + body.isKinematic
                    + "|" + Get(_traceItem!, "LocallyOwned") + "|" + Get(_traceItem!, "RemoteOwner") + "|" + Drive.ActiveStateName
                    + "|" + V(Player.position) + "|" + (Player.parent == null ? "none" : PathOf(Player.parent))
                    + "|" + Get(_traceEngine!, "rpm") + "|" + Get(_traceEngine!, "gear")
                    + "|" + Get(_traceController!, "throttleInput") + "|" + Get(_traceController!, "brakeInput")
                    + "|" + Get(_traceController!, "handbrakeInput") + "|" + _traceParking!.FsmVariables.FindFsmFloat("Brake").Value
                    + "|" + _controlsUntil);
                if (++_traceRows % 10 == 0) _trace.Flush();
            }
            catch (Exception error) { _trace.WriteLine("ERROR|" + error); StopTrace(); }
        }
        private static float Number(string value, float min, float max)
        {
            float parsed = float.Parse(value, CultureInfo.InvariantCulture);
            if (float.IsNaN(parsed) || parsed < min || parsed > max) throw new InvalidOperationException("Invalid driving input.");
            return parsed;
        }
        private static string V(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);
        private static void Snapshot(List<string> rows)
        {
            RecoverySnapshot(rows);
            if (Application.loadedLevelName != "GAME") return;
            var session = SessionManager.Instance!; var car = Car; var body = car.GetComponent<Rigidbody>(); var item = Item;
            rows.Add("session|" + session.State + "|" + session.PlayerCount + "|" + session.LocalPlayerId);
            rows.Add("car|" + Id + "|" + V(body.position) + "|" + V(body.velocity) + "|" + body.isKinematic);
            rows.Add("rotation|" + V(body.rotation.eulerAngles));
            rows.Add("owner|" + Get(item, "LocallyOwned") + "|" + Get(item, "RemoteOwner") + "|" + Get(item, "RemoteIsDriver") + "|" + Get(item, "LocalDriveActive"));
            rows.Add("cabin-proximity|" + ((FsmBool?)Get(item, "PlayerInVar"))?.Value);
            rows.Add("player|" + (Player.parent == null ? "none" : PathOf(Player.parent)) + "|" + V(Player.position) + "|" + Player.GetComponent<CharacterController>()?.enabled);
            rows.Add("passenger|" + Passenger.IsLocalSeated + "|" + Get(Passenger, "_seatedVehicleId") + "|" + Get(Passenger, "_seatedIndex") + "|" + Get(Passenger, "_hint"));
            rows.Add("drive|" + Drive.ActiveStateName + "|" + Drive.GetComponent<Collider>().enabled + "|" + FsmVariables.GlobalVariables.FindFsmBool("PlayerStop")?.Value);
            rows.Add("ignition|" + Ignition.ActiveStateName);
            var parking = Find(CarPath + "/Functions/HandBrake/LeverPivot", "Brake");
            rows.Add("parking|" + parking.ActiveStateName + "|" + parking.FsmVariables.FindFsmFloat("Brake")?.Value
                + "|" + Find(CarPath + "/Functions/HandBrake/LeverPivot", "Use").FsmVariables.FindFsmFloat("KnobPos")?.Value);
            var starter = Find(CarPath + "/Simulation/STARTERxSorbet", "Starter");
            rows.Add("starter|" + starter.ActiveStateName + "|" + starter.FsmVariables.FindFsmFloat("Revs")?.Value);
            foreach (var fsm in car.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm.FsmName != "EngineFriction" && fsm.FsmName != "OperatingTemp" && fsm.FsmName != "Cooling"
                    && fsm.FsmName != "FuelLine" && fsm.FsmName != "Starter") continue;
                rows.Add("engine-fsm|" + PathOf(fsm.transform) + "|" + fsm.FsmName + "|" + fsm.ActiveStateName
                    + "|" + fsm.enabled + "|" + fsm.gameObject.activeInHierarchy);
                foreach (var value in fsm.FsmVariables.FloatVariables)
                    rows.Add("engine-value|" + fsm.FsmName + "|" + value.Name + "|" + value.Value);
                foreach (var value in fsm.FsmVariables.BoolVariables)
                    rows.Add("engine-value|" + fsm.FsmName + "|" + value.Name + "|" + value.Value);
            }
            foreach (var peer in session.Players)
            {
                bool passenger = Passenger.TryGetSeatAnchor(peer.PlayerId, out var seat, out var vehicle);
                rows.Add("peer|" + peer.PlayerId + "|" + passenger + "|" + (seat == null ? "none" : V(seat.position)) + "|" + V(peer.Position));
            }
            foreach (var component in car.GetComponents<Component>())
            {
                if (component == null || (component.GetType().Name != "Drivetrain" && component.GetType().Name != "AxisCarController"
                    && component.GetType().Name != "CarController")) continue;
                rows.Add("component|" + component.GetType().Name + "|" + (component as Behaviour)?.enabled);
                foreach (var field in component.GetType().GetFields(Members))
                    if ((field.FieldType.IsPrimitive || field.FieldType == typeof(string))
                        && (field.Name.ToLowerInvariant().Contains("throttle") || field.Name.ToLowerInvariant().Contains("brake")
                        || field.Name.ToLowerInvariant().Contains("clutch") || field.Name.ToLowerInvariant().Contains("steer")
                        || field.Name.ToLowerInvariant().Contains("gear") || field.Name.ToLowerInvariant().Contains("rpm")
                        || component.GetType().Name == "Drivetrain"))
                        rows.Add("field|" + component.GetType().Name + "|" + field.Name + "|" + field.GetValue(component));
            }
            foreach (var component in car.GetComponentsInChildren<Component>())
            {
                if (component == null || component.GetType().Name != "Wheel") continue;
                string row = "wheel|" + PathOf(component.transform) + "|" + (component as Behaviour)?.enabled;
                foreach (var name in new[] { "brake", "handbrake", "angularVelocity", "onGround", "driveTorque" })
                    row += "|" + name + "=" + component.GetType().GetField(name, Members)?.GetValue(component);
                rows.Add(row);
            }
        }
    }
}
