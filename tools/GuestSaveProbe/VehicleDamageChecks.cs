using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;

namespace WinterMP.GuestSaveProbe
{
    internal static class VehicleDamageChecks
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Assembly Core = typeof(SessionManager).Assembly;
        private static readonly Type Vehicles = Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true);
        private static readonly Type Items = Core.GetType("WinterMP.Core.Sync.ItemWorldSync", true);
        private static readonly Type Item = Core.GetType("WinterMP.Core.Sync.SyncedItem", true);
        private const uint VehicleId = 0x1234;

        internal static void Run(Action<string, Action> check)
        {
            var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
            catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
            var damageRule = catalog.GetProperty("VehicleDamage", Static).GetValue(null, null);
            var references = ((List<string>)Get(damageRule, "PartVariables")).ToArray();
            var guard = Core.GetType("WinterMP.Core.Session.GuestSaveGuard", true);
            Require((bool)guard.GetProperty("ProtectWorld", Static).GetValue(null, null), "Run save checks before the damage probe.");
            var readerType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
            var reader = Activator.CreateInstance(readerType, Members, null, new object[] {
                File.ReadAllText(Path.Combine(Application.dataPath, "../vehicle-damage-probe.json")) }, null);
            var input = (Dictionary<string, object>)readerType.GetMethod("ReadObject", Members).Invoke(reader, null);
            var rows = (List<object>)input["fsms"];
            var damageRow = NativeBagPartChecks.Find(rows, "CORRIS/Simulation/Systems/PartBreakages", "Damages");
            var mountRow = NativeBagPartChecks.Find(rows, "CARPARTS/StartParts/VIN1010/VINP_Headgasket", "Data");
            var root = new GameObject("vehicle damage probe"); root.SetActive(false);
            var body = root.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
            var damageObject = Child(root, "PartBreakages");
            var damage = NativeBagPartChecks.MakeFsm(damageObject, damageRow);
            var mountWear = new FsmFloat?[VehicleDamage.PartSlots];
            var partWear = new FsmFloat?[VehicleDamage.PartSlots];
            var mounts = new PlayMakerFSM?[VehicleDamage.PartSlots];
            var savedParts = new GameObject?[VehicleDamage.PartSlots];
            foreach (var stateValue in (IEnumerable)damageRow["states"])
            {
                var state = (Dictionary<string, object>)stateValue;
                foreach (Dictionary<string, object> action in (IEnumerable)state["actions"])
                    if (((string)action["type"]).EndsWith("MasterAudioPlaySound", StringComparison.Ordinal))
                        action["parameters"] = new List<object>();
            }
            for (int bit = 0; bit < references.Length; bit++)
            {
                if (references[bit].Length == 0) continue;
                var mountObject = Child(root, references[bit]);
                var mount = NativeBagPartChecks.MakeFsm(mountObject, mountRow);
                var saved = Child(mountObject, "saved original " + bit); var data = EmptyData(saved);
                var wear = new FsmFloat { Name = "Wear", UseVariable = true, Value = 70 + bit };
                data.FsmVariables.FloatVariables = new[] { wear };
                mount.FsmVariables.FindFsmGameObject("ActivePart").Value = saved;
                mount.FsmVariables.FindFsmGameObject("AssemblyPoint").Value = mountObject;
                mount.FsmVariables.FindFsmBool("Installed").Value = true;
                mount.FsmVariables.FindFsmFloat("Wear").Value = 70 + bit;
                damage.FsmVariables.FindFsmGameObject(references[bit]).Value = mountObject;
                mounts[bit] = mount; mountWear[bit] = mount.FsmVariables.FindFsmFloat("Wear");
                savedParts[bit] = saved; partWear[bit] = wear;
            }
            var items = Activator.CreateInstance(Items, Members, null, new object?[] { null }, null);
            var vehicles = Activator.CreateInstance(Vehicles, Members, null, new object?[] { null, items }, null);
            var item = Activator.CreateInstance(Item, true);
            Set(item, "Id", VehicleId); Set(item, "Body", body); Set(item, "IsVehicle", true); Set(item, "SystemsReady", true);
            Set(item, "PartBreakagesFsm", damage);
            var registered = (IDictionary)Get(items, "_items");
            try
            {
                root.SetActive(true);
                var power = (PlayMakerFSM)typeof(VehicleStateChecks).GetMethod("MakePower", Static).Invoke(null, new object[] { root });
                NativeBagPartChecks.Start(power);
                Set(item, "ElectricityPowerFsm", power);
                Set(item, "EngineRevsVar", new FsmFloat { Name = "Revs", UseVariable = true, Value = 0 });
                var names = new List<string>();
                foreach (Dictionary<string, object> state in (IEnumerable)damageRow["states"]) names.Add((string)state["name"]);
                NativeBagPartChecks.LoadActions(damage, damageRow, names.ToArray());
                foreach (var state in damage.Fsm.States)
                    for (int i = 0; i < state.Actions.Length; i++)
                        if (state.Actions[i].GetType().Name == "MasterAudioPlaySound")
                        { state.Actions[i] = new QuietAudio(); state.Actions[i].Init(state); }
                foreach (var mount in mounts)
                {
                    if (mount == null) continue;
                    NativeBagPartChecks.LoadActions(mount, mountRow, "Update 2");
                    NativeBagPartChecks.Start(mount);
                }
                NativeBagPartChecks.Start(damage);
                Action reset = () =>
                {
                    NativeBagPartChecks.Fire(damage, "1");
                    for (int bit = 0; bit < references.Length; bit++)
                    {
                        if (mountWear[bit] == null) continue;
                        mountWear[bit]!.Value = 70 + bit; partWear[bit]!.Value = 70 + bit;
                        NativeBagPartChecks.Fire(mounts[bit]!, "Probe idle");
                    }
                };
                Action preserved = () =>
                {
                    for (int bit = 0; bit < references.Length; bit++)
                        if (mountWear[bit] != null)
                            Require(mountWear[bit]!.Value == 70 + bit && partWear[bit]!.Value == 70 + bit
                                && savedParts[bit]!.transform.parent == mounts[bit]!.transform
                                && mounts[bit]!.FsmVariables.FindFsmGameObject("ActivePart").Value == savedParts[bit],
                                "Saved mount/part condition or attachment changed for slot " + bit + ".");
                };
                check("vehicle damage: native headgasket action changes the mount", () =>
                {
                    reset(); damage.SendEvent("HEADGASKET");
                    Require(mountWear[6]!.Value == 0 && partWear[6]!.Value == 76,
                        "Damage fixture did not distinguish the mount scalar from its saved item.");
                });
                check("vehicle damage: native mount propagates its wear into the saved part", () =>
                {
                    NativeBagPartChecks.Fire(mounts[6]!, "Update 2");
                    Require(partWear[6]!.Value == 0, "Native Update 2 did not copy damaged mount wear.");
                    reset();
                });
                check("vehicle damage: native piston failure rolls collateral oilpan or block damage", () =>
                {
                    reset(); damage.SendEvent("PISTON1");
                    Require(mountWear[7]!.Value == 0 && ((mountWear[11]!.Value == 0) != (mountWear[14]!.Value == 0)),
                        "Imported native piston failure did not roll its collateral branch.");
                    reset();
                });
                check("vehicle damage: raw host reader follows the live native mount", () =>
                {
                    var state = (VehicleDamage)CallStatic("ReadDamageState", item, (byte)0)!;
                    Require((state.KnownPartsMask & (1u << 6)) != 0 && state.Wear[6] == 76,
                        "Raw host reader lost the native mount binding.");
                });
                check("vehicle damage: cold guest snapshot does not read local saved condition", () =>
                {
                    Require(Call(vehicles, "TryReadDamageState", item, (byte)0) == null,
                        "Guest advertised native saved wear before receiving host condition.");
                });
                check("vehicle damage: protected guest cannot export a native damage snapshot", () =>
                {
                    ushort sequence = (ushort)Get(item, "OutDamageSequence");
                    Require(Call(vehicles, "TryBuildDamageSnapshot", item, (byte)1) == null
                        && (ushort)Get(item, "OutDamageSequence") == sequence,
                        "Protected guest produced a damage snapshot or advanced host sequencing.");
                });
                check("vehicle damage: protection pauses the native graph before item discovery", () =>
                {
                    Require(registered.Count == 0, "Fixture vehicle was registered too early.");
                    Call(vehicles, "PrepareGuestDamageIsolation");
                    Require(!damage.enabled && !damage.Fsm.RestartOnEnable && damage.ActiveStateName == "1",
                        "Protected guest did not pause the undiscovered native damage FSM.");
                });
                check("vehicle damage: protected graph refuses direct native damage events", () =>
                {
                    foreach (string nativeEvent in new[] { "HEADGASKET", "PISTON1", "BLOCK", "SEIZE", "CAMFAIL" }) damage.SendEvent(nativeEvent);
                    NativeBagPartChecks.Fire(damage, "State 3"); NativeBagPartChecks.Fire(damage, "State 2");
                    preserved(); Require(damage.ActiveStateName == "1", "Protected graph changed its paused playhead.");
                });
                check("vehicle damage: a protected local driver cannot become damage authority", () =>
                {
                    Set(item, "LocallyOwned", true);
                    Require(!(bool)CallStatic("IsDamageAuthority", SessionManager.Instance)!
                        && !(bool)CallStatic("IsDamageAuthority", new object?[] { null })!,
                        "Vehicle ownership or disconnect bypassed latched save protection.");
                });
                check("vehicle damage: host packet is retained before vehicle discovery", () =>
                {
                    Call(vehicles, "ApplyVehicleDamage", Damage(1, 6, -2));
                    var state = (VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)9)!;
                    Require(state != null && state.OwnerPlayerId == 0 && state.Wear[6] == -2,
                        "Host condition was lost while the native vehicle was undiscovered.");
                    preserved();
                });
                registered.Add(VehicleId, item);
                check("vehicle damage: driver receives host condition without native replay", () =>
                {
                    Call(vehicles, "ApplyVehicleDamage", Damage(2, 7, -3));
                    var state = (VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)5)!;
                    Require(state.Wear[7] == -3 && state.DamageMask == (1u << 7), "Local driver did not use the host snapshot.");
                    preserved(); Require(damage.ActiveStateName == "1", "Host snapshot replayed native random damage.");
                });
                check("vehicle damage: observer receives host condition without native writes", () =>
                {
                    Set(item, "LocallyOwned", false); Call(vehicles, "ApplyVehicleDamage", Damage(3, 6, -4));
                    preserved();
                    Require(((VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)0)!).Wear[6] == -4,
                        "Observer lost the accepted host condition.");
                });
                check("vehicle damage: repaired host state clears passive damage", () =>
                {
                    Call(vehicles, "ApplyVehicleDamage", Damage(4, 6, 91));
                    var state = (VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)0)!;
                    Require(state.DamageMask == 0 && state.Wear[6] == 91, "Host repair left a stale passive failure."); preserved();
                });
                check("vehicle damage: guest sender and stale host snapshots cannot overwrite condition", () =>
                {
                    var forged = Damage(5, 6, -9); forged.OwnerPlayerId = 1;
                    Call(vehicles, "ApplyVehicleDamage", forged); Call(vehicles, "ApplyVehicleDamage", Damage(3, 6, -8));
                    Require(((VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)0)!).Wear[6] == 91,
                        "A non-host or stale message changed the passive snapshot."); preserved();
                });
                check("vehicle damage: snapshot copies cannot mutate retained host state", () =>
                {
                    var state = (VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)0)!; state.Wear[6] = -100;
                    Require(((VehicleDamage)Call(vehicles, "TryReadDamageState", item, (byte)0)!).Wear[6] == 91,
                        "Snapshot read exposed mutable retained wear.");
                });
                check("vehicle damage: parked checksum ignores changed saved mount wear", () =>
                {
                    Set(item, "LocallyOwned", false); Set(item, "RemoteOwner", (byte)255);
                    uint before = (uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)!;
                    Require(before != 123u, "Fixture checksum returned before folding the vehicle.");
                    mountWear[6]!.Value = -100;
                    try { Require((uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)! == before,
                        "Guest checksum read damage from its original saved mount."); }
                    finally { mountWear[6]!.Value = 76; }
                });
                check("vehicle damage: parked checksum follows the host failure mask", () =>
                {
                    uint before = (uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)!;
                    Call(vehicles, "ApplyVehicleDamage", Damage(5, 6, -1));
                    Require((uint)Call(vehicles, "FoldVehicleChecksum", 123u, item)! != before,
                        "Guest checksum ignored the accepted host failure mask."); preserved();
                });
                check("vehicle damage: cleanup forgets host condition while retaining native protection", () =>
                {
                    Call(vehicles, "ClearDamageState");
                    Require(Call(vehicles, "TryReadDamageState", item, (byte)0) == null && !damage.enabled
                        && !damage.Fsm.RestartOnEnable && damage.ActiveStateName == "1",
                        "Cleanup resumed an unsafe native graph or retained old host condition.");
                    damage.SendEvent("PISTON1"); preserved();
                });
                check("vehicle damage: repeated preparation preserves the original native playhead", () =>
                {
                    Call(vehicles, "PrepareGuestDamageIsolation"); Call(vehicles, "PrepareGuestDamageIsolation");
                    Require(!damage.enabled && damage.ActiveStateName == "1", "Repeated protection restarted native damage."); preserved();
                });
                check("vehicle damage: initially inactive graphs remain dormant during protection", () =>
                {
                    var dormant = Child(root, "PartBreakages"); dormant.SetActive(false);
                    var fsm = NativeBagPartChecks.MakeFsm(dormant, damageRow);
                    try
                    {
                        Call(vehicles, "SuppressGuestDamage", fsm); Call(vehicles, "ClearDamageState");
                        Require(!fsm.enabled && !fsm.Fsm.Started && !dormant.activeSelf,
                            "Protecting an inactive graph started native actions."); preserved();
                    }
                    finally { UnityEngine.Object.DestroyImmediate(dormant); }
                });
                check("vehicle damage: forced pre-isolation scan pauses graphs created inside the scan interval", () =>
                {
                    Call(vehicles, "PrepareGuestDamageIsolation");
                    var late = Child(root, "PartBreakages"); var fsm = NativeBagPartChecks.MakeFsm(late, damageRow);
                    try
                    {
                        NativeBagPartChecks.Start(fsm); string active = fsm.ActiveStateName;
                        Call(vehicles, "PrepareGuestDamageIsolation");
                        Require(fsm.enabled, "Fixture missed the existing scan interval.");
                        Call(vehicles, "PrepareGuestDamageIsolationNow");
                        Require(!fsm.enabled && !fsm.Fsm.RestartOnEnable && fsm.ActiveStateName == active,
                            "Newly created native damage graph was still active before saved-part isolation."); preserved();
                    }
                    finally { UnityEngine.Object.DestroyImmediate(late); }
                });
                check("vehicle damage: unavailable damage metadata defers readiness until a successful retry", () =>
                {
                    var property = catalog.GetProperty("VehicleDamage", Static);
                    try
                    {
                        property.GetSetMethod(true).Invoke(null, new object?[] { null });
                        Require(!(bool)Call(vehicles, "PrepareGuestDamageIsolationNow")!
                            && !(bool)Call(vehicles, "PrepareGuestDamageIsolation")!,
                            "Unavailable native damage metadata was treated as protected."); preserved();
                    }
                    finally { property.GetSetMethod(true).Invoke(null, new[] { damageRule }); }
                    Require((bool)Call(vehicles, "PrepareGuestDamageIsolationNow")!, "Restored metadata could not retry protection.");
                });
                check("vehicle damage: failed damage preparation preserves a supported fitted original", () =>
                {
                    var property = catalog.GetProperty("VehicleDamage", Static);
                    var partConfig = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
                    object? factoryRule = null;
                    foreach (object candidate in (IEnumerable)Get(partConfig, "Factories"))
                        if ((string)Get(candidate, "Prefix") == "VIN102") factoryRule = candidate;
                    Require(factoryRule != null, "Crankshaft replacement rule missing.");
                    var bridgeType = Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true);
                    var bridge = Activator.CreateInstance(bridgeType, Members, null,
                        new object?[] { null, new Dictionary<PlayMakerFSM, bool>() }, null);
                    var isolatedItems = Activator.CreateInstance(Items, Members, null, new[] { bridge }, null);
                    Call(isolatedItems, "BindVehicles", vehicles);
                    var saved = Child(mounts[5]!.gameObject, "supported saved crankshaft"); var data = EmptyData(saved);
                    data.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = "VIN1027" } };
                    data.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = 1 } };
                    data.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Consumed", UseVariable = true } };
                    data.FsmVariables.GameObjectVariables = new[] { new FsmGameObject { Name = "InstallPoint", UseVariable = true, Value = mounts[5]!.gameObject } };
                    var factory = Activator.CreateInstance(Items.GetNestedType("ReplacementFactory", BindingFlags.NonPublic), true);
                    Set(factory, "Rule", factoryRule!); Set(factory, "Fsm", data);
                    var suppressor = Get(factory, "Suppressor");
                    Require((bool)Call(suppressor, "Suppress", data)!, "Fixture factory was not paused.");
                    var identity = (WinterMP.Net.Sync.ReplacementPartRule)Get(factoryRule!, "Identity");
                    Require(identity.TryId("VIN1027", out uint id), "Fixture native part identity was rejected.");
                    var nativeParts = (IDictionary)Get(isolatedItems, "_nativeParts"); nativeParts.Add(id, data);
                    ((IDictionary)Get(isolatedItems, "_replacementFactories")).Add(identity.FactoryId, factory);
                    var phase = Core.GetType("WinterMP.Core.Sync.NativePartIdentity", true).GetMethod("Phase", Static).Invoke(null, new object[] { data });
                    Require(phase.ToString() == "Fitted", "Fixture part was not a supported fitted candidate.");
                    var occupant = mounts[5]!.FsmVariables.FindFsmGameObject("ActivePart"); var previousOccupant = occupant.Value;
                    occupant.Value = saved;
                    try
                    {
                        property.GetSetMethod(true).Invoke(null, new object?[] { null });
                        Call(isolatedItems, "IsolateGuestParts", SessionManager.Instance);
                        Require(saved.transform.parent == mounts[5]!.transform && saved.activeSelf && nativeParts.Contains(id)
                            && ((IDictionary)Get(isolatedItems, "_isolatedGuestParts")).Count == 0
                            && ((IDictionary)Get(isolatedItems, "_isolatedGuestMounts")).Count == 0
                            && Get(isolatedItems, "_guestPartStorage") == null && !(bool)Get(factory, "Failed")
                            && occupant.Value == saved && mountWear[5]!.Value == 75,
                            "Failed protection moved, hid or discarded the original fitted candidate.");
                    }
                    finally
                    {
                        property.GetSetMethod(true).Invoke(null, new[] { damageRule });
                        occupant.Value = previousOccupant;
                        Call(suppressor, "Restore"); UnityEngine.Object.DestroyImmediate(saved);
                        Call(vehicles, "PrepareGuestDamageIsolationNow");
                    }
                    preserved();
                });
            }
            finally
            {
                registered.Clear(); UnityEngine.Object.DestroyImmediate(root);
                Call(vehicles, "ClearDamageState");
            }
        }

        private static VehicleDamage Damage(ushort sequence, int bit, float wear)
        {
            var state = new VehicleDamage { VehicleId = VehicleId, OwnerPlayerId = 0, Sequence = sequence,
                KnownPartsMask = 1u << bit, DamageMask = wear <= 0 ? 1u << bit : 0 };
            state.Wear[bit] = wear; return state;
        }
        private sealed class QuietAudio : FsmStateAction { public override void OnEnter() { Finish(); } }
        private static PlayMakerFSM EmptyData(GameObject owner)
        {
            var fsm = owner.AddComponent<PlayMakerFSM>(); fsm.enabled = false;
            typeof(PlayMakerFSM).GetField("fsm", Members).SetValue(fsm, new Fsm());
            fsm.Fsm.Name = "Data"; fsm.Fsm.StartState = "Probe idle";
            fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0] } };
            return fsm;
        }
        private static object Get(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object? Call(object target, string name, params object?[] values) => target.GetType().GetMethod(name, Members).Invoke(target, values);
        private static object? CallStatic(string name, params object?[] values) => Vehicles.GetMethod(name, Static).Invoke(null, values);
        private static GameObject Child(GameObject parent, string name)
        { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
