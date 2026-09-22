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
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed class Variant
        {
            internal string NativeId = "";
            internal uint PartId, FactoryId, Revision;
            internal PlayMakerFSM Part = null!, Mount = null!;
            internal object Binding = null!, Factory = null!;
        }

        private sealed class AuxiliaryInput
        {
            internal PlayMakerFSM Mount = null!, Original = null!;
            internal readonly List<Variant> Variants = new List<Variant>();
        }

        private sealed partial class Fixture : IDisposable
        {
            internal readonly GameObject Car, Anchor, Controller, Extras;
            internal PlayMakerFSM Reader;
            internal readonly PlayMakerFSM Mount, Original, Part, Starter, Boost;
            internal readonly Transform Mesh;
            internal readonly SessionManager Session;
            internal readonly object Sync, Bridge, Binding, Factory;
            internal readonly uint PartId, AnchorId, FactoryId;
            internal readonly FsmGameObject Shared;
            internal readonly FsmStateAction[] Readers;
            internal readonly object[] OriginalOwners;
            private readonly bool _starter, _water, _cooling, _fuel, _oil, _cam, _alternator, _electrics, _fanbelt, _timingbelt, _powertrain, _fluids, _fan, _flywheel;
            private readonly string _familyPrefix;
            private readonly bool _vehicleMount, _revlimiter, _coil;
            private const uint CarId = 902148;
            private readonly Dictionary<string, AuxiliaryInput> _auxiliaryInputs = new Dictionary<string, AuxiliaryInput>();
            private readonly Dictionary<string, PlayMakerFSM> _anchors = new Dictionary<string, PlayMakerFSM>();
            internal readonly List<Variant> FlywheelVariants = new List<Variant>();
            internal readonly List<Variant> CamVariants = new List<Variant>();
            internal Variant? Alternative;
            private readonly string _nativeId, _mountName, _mountRelativePath;
            private readonly object _world;
            private readonly object? _savedSession, _savedWorld;
            private readonly Dictionary<string, object> _row;
            private readonly List<object> _rows;
            private uint _revision;

            internal Fixture(string familyPrefix = "VIN131", string? consumer = null, string? fixture = null)
            {
                _ambientProbe = fixture == "cooling-ambient-input-probe.json"; _familyPrefix = familyPrefix; _coil = familyPrefix == "VIN212"; _revlimiter = familyPrefix == "REVLIMITER0"; _flywheel = familyPrefix == "VIN120"; _fan = familyPrefix == "VIN137"; _starter = familyPrefix == "VIN130"; _water = familyPrefix == "VIN126";
                _cooling = consumer == "Cooling"; _fuel = familyPrefix == "VIN125"; _oil = familyPrefix == "VIN132";
                _cam = familyPrefix == "VIN115"; _alternator = familyPrefix == "VIN133"; _electrics = consumer == "Electrics"; _fanbelt = familyPrefix == "FANBELT0"; _timingbelt = familyPrefix == "VIN107";
                _powertrain = familyPrefix == "VIN102" || familyPrefix == "VIN105" || familyPrefix == "VIN109" || familyPrefix == "VIN110";
                _fluids = familyPrefix == "VIN134" || familyPrefix == "VIN129" || familyPrefix == "VIN128" || familyPrefix == "OILFILTR0";
                _savedSession = SessionManager.Instance; _savedWorld = World.GetProperty("Instance", Static).GetValue(null, null);
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true); catalog.GetMethod("EnsureLoaded", Static).Invoke(null, null);
                object? input = null;
                foreach (object candidate in (IEnumerable)Get(catalog.GetProperty("GuestEngineInputs", Static).GetValue(null, null), "Entries"))
                    if ((string)Get(candidate, "FamilyPrefix") == familyPrefix && (consumer == null || (string)Get(candidate, "Fsm") == consumer)) { input = candidate; break; }
                Require(input != null, "Missing primary engine input profile.");
                string prefix = familyPrefix; _nativeId = prefix + "7";
                string mountPath = (string)Get(input!, "MountPath"); _mountName = mountPath.Substring(mountPath.LastIndexOf('/') + 1);
                _vehicleMount = mountPath.StartsWith("CORRIS/", StringComparison.Ordinal);
                string anchorNativeId = _vehicleMount ? "CORRIS" : mountPath.Split('/')[2];
                _mountRelativePath = mountPath.Substring((_vehicleMount ? "CORRIS/" : "CARPARTS/StartParts/" + anchorNativeId + "/").Length);
                var readerRules = (IList)Get(input!, "Readers"); Readers = new FsmStateAction[readerRules.Count]; OriginalOwners = new object[Readers.Length];
                var c = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
                object? rule = null; foreach (object candidate in (IEnumerable)Get(c, "Factories")) if ((string)Get(candidate, "Prefix") == prefix) rule = candidate;
                Require(rule != null, "Engine input family catalog missing."); FactoryId = (uint)Get(Get(rule!, "Identity"), "FactoryId");
                var jsonType = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var json = Activator.CreateInstance(jsonType, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath,
                    fixture != null ? "../" + fixture : _flywheel ? "../flywheel-engine-input-probe.json" : _fluids ? "../fluid-engine-input-probe.json" : _powertrain ? "../powertrain-engine-input-probe.json" : _timingbelt ? "../timingbelt-engine-input-probe.json" : _fanbelt ? "../fanbelt-engine-input-probe.json" : _electrics ? "../alternator-electrical-input-probe.json" : _alternator ? "../alternator-mechanical-input-probe.json" : _cam ? "../camshaft-engine-input-probe.json" : _oil ? "../oilpump-engine-input-probe.json" : _fuel ? "../fuelpump-engine-input-probe.json" : _water ? "../waterpump-engine-input-probe.json" : _starter ? "../starter-engine-input-probe.json" : "../guest-engine-input-probe.json")) }, null);
                _rows = (List<object>)((Dictionary<string, object>)jsonType.GetMethod("ReadObject", Members).Invoke(json, null))["fsms"];
                _row = NativeBagPartChecks.Find(_rows, (string)Get(input!, "ReaderPath"), (string)Get(input!, "Fsm"));
                Controller = new GameObject("engine input controller"); Controller.SetActive(false);
                Car = new GameObject("CORRIS"); Car.SetActive(false); Extras = new GameObject("engine input saved objects"); Extras.SetActive(false);
                PlayMakerFSM? anchorData = null;
                Anchor = _vehicleMount ? Car : Child(Extras, anchorNativeId);
                if (!_vehicleMount) { anchorData = Data(Anchor, anchorNativeId, 1); _anchors.Add(anchorNativeId, anchorData); }
                Mount = Data(PathObject(Anchor, anchorNativeId + "/" + _mountRelativePath), null, 0); Seed(Mount, 77, 8, 2, true);
                Original = Data(Child(Extras, "saved engine part"), prefix + "0", 1); Seed(Original, 77, 8, 2, true);
                Mesh = Child(Original.gameObject, "saved mesh").transform; Mesh.localRotation = Quaternion.Euler(0, 0, 2);
                var hand = Empty(Original.gameObject, "HandRotate"); hand.FsmVariables.GameObjectVariables = new[] { ObjectVar("MeshRotate", Mesh.gameObject) };
                Mount.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", Original.gameObject) };
                Part = Data(Child(Mount.gameObject, "host engine part replica"), _nativeId, 1); Seed(Part, 90, 8, 12, true);
                if (_fanbelt) Part.gameObject.AddComponent<Rigidbody>().isKinematic = true;
                var factoryFsm = Empty(Child(Extras, "engine part factory"), "Create"); factoryFsm.FsmVariables.GameObjectVariables = new[] { ObjectVar("VINP", Mount.gameObject) };
                Reader = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)_row["path"]), _row);
                foreach (var v in Reader.FsmVariables.GameObjectVariables) v.Value = Mount.gameObject;
                Shared = Reader.FsmVariables.FindFsmGameObject((string)Get(input!, "TargetVariable"));
                var coil = Data(Child(Extras, "coil and wire"), null, 0); Seed(coil, 100, 8, 0, true);
                if (_starter)
                {
                    var booleans = new List<FsmBool>(coil.FsmVariables.BoolVariables);
                    booleans.Add(new FsmBool { Name = "Bolted", UseVariable = true, Value = true });
                    coil.FsmVariables.BoolVariables = booleans.ToArray();
                    foreach (string wire in new[] { "db_WiringStarter", "db_WiringBatteryHarness", "db_WiringGround" })
                        Reader.FsmVariables.FindFsmGameObject(wire).Value = coil.gameObject;
                }
                else if (Reader.FsmName == "Cylinders")
                {
                    Reader.FsmVariables.FindFsmGameObject("db_IgnitionCoil").Value = coil.gameObject;
                    Reader.FsmVariables.FindFsmGameObject("w_IgnitionCoil").Value = coil.gameObject;
                }
                Starter = Empty(Child(Extras, "starter stop target"), "Starter"); Starter.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "ShutOff", UseVariable = true } };
                Boost = Empty(Child(Extras, "boost target"), "Boost"); Boost.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "SparkRetard", UseVariable = true } };
                Session = Controller.AddComponent<SessionManager>(); Session.enabled = false;
                _world = Controller.AddComponent(World); ((Behaviour)_world).enabled = false;
                Bridge = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.WorldSyncBridge", true), Members, null, new object[] { _world, new Dictionary<PlayMakerFSM, bool>() }, null);
                Sync = Activator.CreateInstance(Items, Members, null, new[] { Bridge }, null);
                var vehicles = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.VehicleWorldSync", true), Members, null, new[] { Bridge, Sync }, null);
                var fsms = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.FsmWorldSync", true), Members, null, new[] { Bridge, vehicles }, null);
                Call(Sync, "BindVehicles", vehicles); Call(Bridge, "BindItems", Sync); Call(Bridge, "BindFsms", fsms);
                Set(_world, "_bridge", Bridge); Set(_world, "_items", Sync); Set(_world, "_vehicles", vehicles); Set(_world, "_fsm", fsms); Set(_world, "_syncReady", true);
                Factory = Nested("ReplacementFactory"); Set(Factory, "Rule", rule); Set(Factory, "Fsm", factoryFsm); Set(Factory, "TemplateData", Part); Set(Factory, "Prefab", Part.gameObject);
                ((IDictionary)Get(Sync, "_replacementFactories")).Add(FactoryId, Factory);
                Binding = Nested("ReplacementBinding"); Set(Binding, "Factory", Factory); Set(Binding, "Data", Part); Set(Binding, "Replica", true); Set(Binding, "NativeId", _nativeId);
                PartIdentity.TryItemId(_nativeId, out PartId);
                if (_vehicleMount) AnchorId = CarId; else PartIdentity.TryItemId(anchorNativeId, out AnchorId);
                ((IDictionary)Get(Sync, "_replacementParts")).Add(PartId, Binding);
                if (anchorData != null) ((IDictionary)Get(Sync, "_nativeParts")).Add(AnchorId, anchorData);
                var carBody = Car.AddComponent<Rigidbody>(); carBody.isKinematic = true;
                var carItem = Activator.CreateInstance(Core.GetType("WinterMP.Core.Sync.SyncedItem", true), true);
                Set(carItem, "Id", CarId); Set(carItem, "Body", carBody); Set(carItem, "IsVehicle", true); Set(carItem, "Path", "CORRIS");
                ((IDictionary)Get(Sync, "_items")).Add(CarId, carItem);
                Call(Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(Bridge, null), "MarkReplica", Part);
                if (_fuel || _alternator) Alternative = AddVariant(_alternator ? "ALTERNATOR0" : "FUELPUMP0");
                if (_flywheel)
                    foreach (string variant in new[] { "FLYWHEELa0", "FLYWHEELb0", "VIN138" }) FlywheelVariants.Add(AddVariant(variant));
                if (_cam)
                    foreach (string variant in new[] { "CAMTUNEa0", "CAMTUNEb0", "CAMTUNEc0", "CAMTUNEd0" }) CamVariants.Add(AddVariant(variant));
                Controller.SetActive(true); Car.SetActive(true); Extras.SetActive(true);
                Load(catalog); foreach (var fsm in Extras.GetComponentsInChildren<PlayMakerFSM>(true)) if (!fsm.Fsm.Started) NativeBagPartChecks.Start(fsm);
                foreach (var fsm in Car.GetComponentsInChildren<PlayMakerFSM>(true)) if (fsm != Reader && !fsm.Fsm.Started) NativeBagPartChecks.Start(fsm);
                Call(Get(Factory, "Suppressor"), "Suppress", factoryFsm);
                if (Alternative != null) Call(Get(Alternative.Factory, "Suppressor"), "Suppress", Get(Alternative.Factory, "Fsm"));
                foreach (var variant in FlywheelVariants) Call(Get(variant.Factory, "Suppressor"), "Suppress", Get(variant.Factory, "Fsm"));
                foreach (var variant in CamVariants) Call(Get(variant.Factory, "Suppressor"), "Suppress", Get(variant.Factory, "Fsm"));
                NativeBagPartChecks.Start(Reader);
                for (int i = 0; i < Readers.Length; i++)
                {
                    Readers[i] = Action((string)Get(readerRules[i], "State"), (int)Get(readerRules[i], "ActionIndex"));
                    OriginalOwners[i] = Get(Readers[i], "gameObject");
                }
                StaticProperty(typeof(SessionManager), "Instance", Session); StaticProperty(World, "Instance", _world);
                Property(Session, "IsHost", false); Property(Session, "State", SessionState.Connected); Property(Session, "LocalPlayerId", (byte)3);
                if (!_ambientProbe) SeedInitialAmbient();
                SeedAuxiliaryPrerequisites();
            }
            internal bool Installed => _coil || _revlimiter || _flywheel || _powertrain || _fluids || _fan ? ProxyData.FsmVariables.FindFsmBool("Installed").Value : Reader.FsmName == "Wearing" || Reader.FsmName == "Valves" ? ProxyData.FsmVariables.FindFsmBool("Installed").Value
                : Reader.FsmVariables.FindFsmBool(_fanbelt ? (_electrics ? "Installed2" : "Installed1") : _cam ? "Installed4" : _fuel || _water || _oil || _alternator ? "Installed1" : _starter ? "Installed5" : "Installed2").Value;
            internal float Durability => Reader.FsmVariables.FindFsmFloat(_electrics ? "DurabilityAlternator" : _cam ? "DurabilityCamshaft" : _oil ? "DurabilityOilpump" : _fuel ? "DurabilityFuelpump" : _water ? "DurabilityWaterPump" : "StarterDurability").Value;
            internal float Angle => Reader.FsmVariables.FindFsmFloat("Angle").Value;
            internal float Tightness => Reader.FsmVariables.FindFsmFloat(_cooling ? "Tightness1" : "Tightness").Value;
            internal float Efficiency => Reader.FsmVariables.FindFsmFloat("WaterPumpEfficiency").Value;
            // RPM is global in the game; this isolated graph controls its native
            // comparison input without creating a shadow local variable.
            internal float PumpRpm { set { ((FsmFloat)Get(Action("Water Pump 2", 2), "float1")).Value = value; } }
            internal float Wear => Reader.FsmVariables.FindFsmFloat(_familyPrefix == "OILFILTR0" ? "Dirt" : _powertrain && Reader.FsmName == "Wearing" ? "Condition" : _powertrain && Reader.FsmName == "FuelLine" ? "AuxShaftWear" : _electrics ? "AlternatorCondition" : _fuel ? "FuelpumpWear" : "Wear").Value;
            internal float OutputRate => Reader.FsmVariables.FindFsmFloat("PumpRate").Value;
            internal GameObject Proxy => Target(Readers[0]);
            internal PlayMakerFSM ProxyData { get { foreach (var fsm in Proxy.GetComponents<PlayMakerFSM>()) if (fsm.FsmName == "Data") return fsm; throw new InvalidOperationException("Proxy Data missing."); } }
            internal GameObject Target(FsmStateAction action) => Reader.Fsm.GetOwnerDefaultTarget((FsmOwnerDefault)Get(action, "gameObject"));
            internal FsmStateAction Action(string state, int index) => NativeBagPartChecks.State(Reader, state).Actions[index];
            internal bool Prepare() => (bool)Call(Sync, "PrepareGuestEngineInputs", true, false)!;
            internal void ReadAll() { foreach (var action in Readers) action.OnEnter(); }
            internal void Fire(string state) => NativeBagPartChecks.Fire(Reader, state);
            internal void Receive(float wear, float tightness, float angle, bool applied, bool fitted = true, float efficiency = 1.8f, string camProfile = "55003500", float durability = .7f, float electricalEfficiency = 350f, bool damaged = false)
            {
                var state = new ReplacementPartState { FactoryId = FactoryId, NativeId = _nativeId, Revision = ++_revision,
                    Scalars = _revlimiter ? new[] { tightness, angle } : _coil || _fanbelt || _timingbelt || _powertrain || _fluids || _fan ? new[] { wear, tightness } : _alternator ? new[] { wear, tightness, angle, efficiency, durability, electricalEfficiency } : _water || _fuel || _cam ? new[] { wear, tightness, angle, efficiency } : new[] { wear, tightness, angle }, Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity,
                    CamProfile = _cam ? camProfile : string.Empty, AlternatorDamaged = _alternator && fitted ? damaged : (bool?)null,
                    LocalScale = new NetVector3(1, 1, 1), AssemblyId = fitted ? 1 : 0, Installed = fitted,
                    ParentKind = fitted ? (_vehicleMount ? PartParentKind.Vehicle : PartParentKind.NativePart) : PartParentKind.None, ParentId = fitted ? AnchorId : 0,
                    ParentPath = fitted ? _mountRelativePath : string.Empty };
                Call(Sync, "OnReplacementPartState", state);
                Require(((ReplacementPartReplica)Get(Sync, "_replacementReplica")).Get(PartId)?.Revision == _revision, "Host fixture state was rejected.");
                if (applied) MarkApplied();
            }
            internal void PublishWrongAttachment(PartParentKind kind, uint id, string path)
            {
                var state = ((ReplacementPartReplica)Get(Sync, "_replacementReplica")).Get(PartId)!;
                state.Revision = ++_revision; state.ParentKind = kind; state.ParentId = id; state.ParentPath = path;
                Call(Sync, "OnReplacementPartState", state); MarkApplied();
            }
            internal Dictionary<string, object> FindNativeRow(string path, string fsm) => NativeBagPartChecks.Find(_rows, path, fsm);
            internal void MarkApplied()
            {
                ((HashSet<uint>)Get(Sync, "_pendingReplacements")).Remove(PartId); Set(Binding, "HasAppliedState", true);
                Set(Binding, "AppliedRevision", _revision); Set(Binding, "FittedPresentation", true);
            }
            private Variant AddVariant(string prefix, PlayMakerFSM? targetMount = null, string? nativeId = null)
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                var config = catalog.GetProperty("ReplacementParts", Static).GetValue(null, null);
                object? rule = null; foreach (object candidate in (IEnumerable)Get(config, "Factories")) if ((string)Get(candidate, "Prefix") == prefix) rule = candidate;
                Require(rule != null, "Missing alternative pump factory.");
                var value = new Variant { NativeId = nativeId ?? prefix + "7", FactoryId = (uint)Get(Get(rule!, "Identity"), "FactoryId"), Mount = targetMount ?? Mount };
                PartIdentity.TryItemId(value.NativeId, out value.PartId);
                value.Part = Data(Child(value.Mount.gameObject, "additional host part"), value.NativeId, 1); Seed(value.Part, 95, 16, 0, true);
                var factories = (IDictionary)Get(Sync, "_replacementFactories");
                if (factories.Contains(value.FactoryId)) value.Factory = factories[value.FactoryId];
                else
                {
                    var factory = Empty(Child(Extras, "alternative part factory"), "Create"); factory.FsmVariables.GameObjectVariables = new[] { ObjectVar("VINP", value.Mount.gameObject) };
                    value.Factory = Nested("ReplacementFactory"); Set(value.Factory, "Rule", rule); Set(value.Factory, "Fsm", factory);
                    Set(value.Factory, "TemplateData", value.Part); Set(value.Factory, "Prefab", value.Part.gameObject);
                    factories.Add(value.FactoryId, value.Factory);
                }
                value.Binding = Nested("ReplacementBinding"); Set(value.Binding, "Factory", value.Factory); Set(value.Binding, "Data", value.Part);
                Set(value.Binding, "Replica", true); Set(value.Binding, "NativeId", value.NativeId);
                ((IDictionary)Get(Sync, "_replacementParts")).Add(value.PartId, value.Binding);
                Call(Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(Bridge, null), "MarkReplica", value.Part);
                return value;
            }
            internal void ReceiveVariant(Variant variant, float wear, float durability, float outputRate, bool applied, bool fitted = true, string camProfile = "60003840", float electricalEfficiency = 293f, bool damaged = false)
            {
                var scalars = (string[])Get(Get(variant.Factory, "Rule"), "Scalars"); var values = new float[scalars.Length];
                for (int i = 0; i < values.Length; i++) values[i] = scalars[i] == "Wear" || scalars[i] == "Dirt" ? wear : scalars[i] == "Tightness" ? ((variant.NativeId.StartsWith("FANBELT0", StringComparison.Ordinal) || variant.NativeId.StartsWith("VIN107", StringComparison.Ordinal)) ? 0f : 16f)
                    : scalars[i] == "Durability" ? durability : scalars[i] == "Efficiency" && (bool)Get(Get(Get(variant.Factory, "Rule"), "Identity"), "SupportsAlternatorDamage") ? electricalEfficiency : outputRate;
                MountAddress(variant.Mount, out uint parentId, out string parentPath);
                var state = new ReplacementPartState { FactoryId = variant.FactoryId, NativeId = variant.NativeId, Revision = ++variant.Revision,
                    Scalars = values, Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity,
                    AlternatorDamaged = (bool)Get(Get(Get(variant.Factory, "Rule"), "Identity"), "SupportsAlternatorDamage") && fitted ? damaged : (bool?)null,
                    CamProfile = (string?)Get(Get(variant.Factory, "Rule"), "CamProfileVariable") != null ? camProfile : string.Empty,
                    LocalScale = new NetVector3(1, 1, 1), AssemblyId = fitted ? 1 : 0, Installed = fitted,
                    ParentKind = fitted ? (parentId == CarId ? PartParentKind.Vehicle : PartParentKind.NativePart) : PartParentKind.None, ParentId = fitted ? parentId : 0,
                    ParentPath = fitted ? parentPath : string.Empty };
                Call(Sync, "OnReplacementPartState", state);
                Require(((ReplacementPartReplica)Get(Sync, "_replacementReplica")).Get(variant.PartId)?.Revision == variant.Revision, "Alternative pump state was rejected.");
                if (applied)
                {
                    ((HashSet<uint>)Get(Sync, "_pendingReplacements")).Remove(variant.PartId); Set(variant.Binding, "HasAppliedState", true);
                    Set(variant.Binding, "AppliedRevision", variant.Revision); Set(variant.Binding, "FittedPresentation", true);
                }
            }
            internal void AssertVariantReady(Variant variant)
            {
                var state = ((ReplacementPartReplica)Get(Sync, "_replacementReplica")).Get(variant.PartId);
                Require(state != null, "Additional replica has no accepted state.");
                Require(variant.Part.gameObject.activeInHierarchy, "Additional replica is inactive.");
                Require((bool)Get(variant.Binding, "Replica") && (bool)Get(variant.Binding, "HasAppliedState")
                    && (uint)Get(variant.Binding, "AppliedRevision") == state!.Revision, "Additional replica revision is not applied.");
                Require(!((HashSet<uint>)Get(Sync, "_pendingReplacements")).Contains(variant.PartId), "Additional replica remains pending.");
                Require((bool)Get(variant.Binding, "FittedPresentation"), "Additional replica is not fitted.");
                Require((bool)Get(Get(variant.Factory, "Suppressor"), "_active"), "Additional factory is not suppressed.");
                Require(variant.Part.transform.parent == variant.Mount.transform, "Additional replica has the wrong actual parent.");
                Require((Transform?)Call(Sync, "ResolveReplacementParent", state) == variant.Mount.transform,
                    "Additional attachment did not resolve: " + state!.ParentPath + " expected " + variant.Mount.gameObject.name);
                Require((bool)Call(Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(Bridge, null), "IsReplica", variant.Part)!, "Additional replica ownership was lost.");
                Require(variant.Part.FsmVariables.FindFsmString("ID").Value == state!.NativeId, "Additional native identity was replaced.");
                Require(variant.Part.Fsm.Initialized && variant.Part.Fsm.Started, "Additional replica native Data is not initialized/started.");
                var identity = Bridge.GetType().GetProperty("PartIdentities", Members).GetValue(Bridge, null);
                object?[] rootArgs = { variant.Part, (uint)0 };
                Require((bool)Call(identity, "TryRootId", rootArgs)! && (uint)rootArgs[1]! == variant.PartId, "Additional persistent identity does not resolve.");
                object?[] mountArgs = { variant.Mount.transform, PartParentKind.None, (uint)0, "" };
                Require((bool)Call(Sync, "TryMountAddress", mountArgs)! && (uint)mountArgs[2]! == state.ParentId
                    && (string)mountArgs[3]! == state.ParentPath, "Additional live mount address does not match the accepted attachment.");
            }
            internal void AssertSaved()
            {
                AssertSavedWires(); AssertSavedBattery(); AssertSavedEngineBlock(); AssertSavedGearbox(); AssertSavedCylinderHead(); AssertSavedCarburettor(); AssertSavedAirCleaner(); AssertSavedExhaust(); AssertSavedValves(); AssertSavedOilpan(); AssertSavedRockerCover(); AssertSavedRadiator(); AssertSavedCoolantHoses(); AssertSavedAirflow();
                var saved = new List<PlayMakerFSM> { Original, Mount };
                foreach (var auxiliary in _auxiliaryInputs.Values) { saved.Add(auxiliary.Mount); saved.Add(auxiliary.Original); }
                foreach (var data in saved) Require(data.FsmVariables.FindFsmBool("Installed").Value
                    && data.FsmVariables.FindFsmFloat("Wear").Value == 77 && data.FsmVariables.FindFsmFloat("Tightness").Value == 8
                    && data.FsmVariables.FindFsmFloat("SparkAngle").Value == 2
                    && data.FsmVariables.FindFsmFloat("InertiaFactor").Value == .2f
                    && data.FsmVariables.FindFsmFloat("SettingRPM").Value == 6200f
                    && data.FsmVariables.FindFsmFloat("Dirt").Value == 6
                    && data.FsmVariables.FindFsmFloat("Friction").Value == 4.2f && data.FsmVariables.FindFsmFloat("SettingRotation").Value == 7
                    && data.FsmVariables.FindFsmFloat("Durability").Value == 2 && data.FsmVariables.FindFsmFloat("Efficiency").Value == 3 && data.FsmVariables.FindFsmFloat("OutputRate").Value == 400
                    && data.FsmVariables.FindFsmFloat("ValveTolerance").Value == 3 && data.FsmVariables.FindFsmString("CamProfile").Value == "51003100", "Guest input projection changed retained saved Data.");
                foreach (var auxiliary in _auxiliaryInputs.Values)
                    Require(auxiliary.Mount.FsmVariables.FindFsmGameObject("ActivePart").Value == auxiliary.Original.gameObject, "Additional saved ActivePart changed.");
                Require(Mount.FsmVariables.FindFsmGameObject("ActivePart").Value == Original.gameObject && Shared.Value == Mount.gameObject
                    && Quaternion.Angle(Mesh.localRotation, Quaternion.Euler(0, 0, 2)) < .001f, "Guest input projection changed native mount references or saved mesh.");
            }
            internal void CheckUnavailable(string change)
            {
                Receive(90, 8, 11, true); Prepare();
                if (change == "pending") ((HashSet<uint>)Get(Sync, "_pendingReplacements")).Add(PartId);
                if (change == "revision") Set(Binding, "AppliedRevision", _revision - 1);
                if (change == "hidden") Set(Binding, "FittedPresentation", false);
                if (change == "foreign parent") Part.transform.SetParent(Extras.transform, false);
                if (change == "inactive part") Part.gameObject.SetActive(false);
                if (change == "changed native ID") Part.FsmVariables.FindFsmString("ID").Value = "VIN13199";
                if (change == "failed factory") Set(Factory, "Failed", true);
                try
                {
                    bool ready = Prepare();
                    if (change == "failed factory") Require(!ready && !Reader.enabled, "Failed native factory did not pause its consumer.");
                    else { Require(ready, "Safe unavailable replica failed preparation: " + change); ReadAll(); Require(!Installed, "Unavailable applied replica supplied Installed: " + change); }
                    AssertSaved();
                }
                finally { Part.gameObject.SetActive(true); Part.transform.SetParent(Mount.transform, false); Part.FsmVariables.FindFsmString("ID").Value = "VIN1317"; Set(Factory, "Failed", false); MarkApplied(); Prepare(); }
            }
            internal void CheckChanged(string field)
            {
                var action = Readers[1]; var original = Get(action, field);
                object changed = field == "everyFrame" ? (object)true : field == "storeValue" ? new FsmFloat { Name = "Angle", UseVariable = true }
                    : new FsmString { Value = "Other", UseVariable = false };
                Set(action, field, changed);
                try { Require(!Prepare() && !Reader.enabled && Part.gameObject.activeSelf, "Malformed native input escaped scoped containment: " + field); AssertSaved(); }
                finally { Set(action, field, original); if (!Prepare()) Prepare(); }
                Require(Reader.enabled, "Repaired reader remained paused: " + field); ReadAll(); Require(Installed && Angle == 11, "Repaired reader did not resume host input.");
            }
            internal FsmStateAction Import(string state, int index)
            {
                foreach (Dictionary<string, object> row in (IEnumerable)_row["states"])
                    if ((string)row["name"] == state) return NativeAction((Dictionary<string, object>)((List<object>)row["actions"])[index], Reader);
                throw new InvalidOperationException("Missing native state.");
            }
            internal void RecreateBlockedReader()
            {
                Reader = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)_row["path"]), _row);
                foreach (var value in Reader.FsmVariables.GameObjectVariables) value.Value = Mount.gameObject;
                Load(Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true)); NativeBagPartChecks.Start(Reader);
            }
            internal PlayMakerFSM AddConsumer(string name)
            {
                Dictionary<string, object>? row = null;
                foreach (Dictionary<string, object> candidate in _rows) if ((string)candidate["fsmName"] == name) row = candidate;
                Require(row != null, "Missing companion native graph.");
                var reader = NativeBagPartChecks.MakeFsm(PathObject(Car, (string)row!["path"]), row);
                foreach (var variable in reader.FsmVariables.GameObjectVariables) variable.Value = Mount.gameObject;
                Load(Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true), reader, row); NativeBagPartChecks.Start(reader);
                SeedAuxiliaryPrerequisites();
                return reader;
            }
            private void SeedAuxiliaryPrerequisites()
            {
                if (SavedCarburettor != null && !_carburettorSeeded) { SetCarburettor(true, 25, .1f, 14.5f); _carburettorSeeded = true; }
                if (SavedAirCleaner != null && !_airCleanerSeeded) { SeedAirCleaner(); _airCleanerSeeded = true; }
                if (_savedExhaust.Count != 0 && !_exhaustSeeded) { SeedExhaust(); _exhaustSeeded = true; }
                if (SavedCylinderHead != null && _engineBlockRevision == 0) SetCylinderHead(true);
                if (SavedGearbox != null && _gearboxRevision == 0) SetGearbox(0);
                if (SavedEngineBlock != null && _engineBlockRevision == 0) SetEngineBlock(true, 90, false);
                if (SavedBattery != null && _batteryRevision == 0) SetBattery(true, 127);
                foreach (uint id in _savedWires.Keys)
                    if (!_wireRevisions.ContainsKey(id)) SetWire(id, true, WiringPolicy.SupportsBolted(id));
                // Companion-part checks provide explicit applied host prerequisites;
                // saved mounts are never substituted for these engine inputs.
                foreach (string prefix in new[] { "VIN212", "VIN137", "FANBELT0", "VIN107", "VIN102", "VIN105", "VIN109", "VIN110", "VIN134", "VIN129", "VIN128", "OILFILTR0" })
                    if (_auxiliaryInputs.TryGetValue(prefix, out var belt) && belt.Variants[0].Revision == 0)
                        ReceiveVariant(belt.Variants[0], 90, 0, 0, true);
                if (_auxiliaryInputs.TryGetValue("VIN120", out var flywheel) && flywheel.Variants[0].Revision == 0)
                    ReceiveVariant(flywheel.Variants[0], 90, 0, .1f, true);
                foreach (var plug in Sparkplugs)
                    if (plug != null && plug.Revision == 0) ReceiveSparkplug(plug.Part.FsmVariables.FindFsmInt("AssemblyID").Value, 100);
            }
            private void Load(Type catalog) => Load(catalog, Reader, _row);
            internal AuxiliaryInput Auxiliary(string prefix) => _auxiliaryInputs[prefix];
            private void MountAddress(PlayMakerFSM mount, out uint id, out string path)
            {
                foreach (var anchor in _anchors)
                {
                    var relative = mount.transform; path = "";
                    while (relative != null && relative != anchor.Value.transform)
                    { path = relative.name + (path.Length == 0 ? "" : "/" + path); relative = relative.parent; }
                    if (relative == anchor.Value.transform) { PartIdentity.TryItemId(anchor.Key, out id); return; }
                }
                var vehicleRelative = mount.transform; path = "";
                while (vehicleRelative != null && vehicleRelative != Car.transform)
                { path = vehicleRelative.name + (path.Length == 0 ? "" : "/" + path); vehicleRelative = vehicleRelative.parent; }
                if (vehicleRelative == Car.transform) { id = CarId; return; }
                throw new InvalidOperationException("Fixture mount has no registered anchor.");
            }
            private GameObject MountObject(string path)
            {
                if (path.StartsWith("CORRIS/", StringComparison.Ordinal)) return PathObject(Car, path);
                string nativeId = path.Split('/')[2];
                if (!_anchors.TryGetValue(nativeId, out var anchor))
                {
                    anchor = Data(Child(Extras, nativeId), nativeId, 1); _anchors.Add(nativeId, anchor);
                    PartIdentity.TryItemId(nativeId, out uint id); ((IDictionary)Get(Sync, "_nativeParts")).Add(id, anchor);
                    NativeBagPartChecks.Start(anchor);
                }
                return PathObject(anchor.gameObject, path.Substring("CARPARTS/StartParts/".Length));
            }
            private void ConfigureSources(PlayMakerFSM reader)
            {
                var catalog = Core.GetType("WinterMP.Core.Catalog.SyncCatalog", true);
                var profile = catalog.GetProperty("GuestEngineInputs", Static).GetValue(null, null);
                if (reader.FsmVariables.FindFsmGameObject("db_Battery") != null)
                    reader.FsmVariables.FindFsmGameObject("db_Battery").Value = EnsureSavedBattery().gameObject;
                foreach (object entry in (IEnumerable)Get(profile, "Entries"))
                {
                    if ((string)Get(entry, "Fsm") != reader.FsmName) continue;
                    string prefix = (string)Get(entry, "FamilyPrefix");
                    if (Get(entry, "BlockSource") != null)
                    {
                        var block = EnsureSavedEngineBlock();
                        if (!(bool)Get(entry, "DirectTarget")) reader.FsmVariables.FindFsmGameObject((string)Get(entry, "TargetVariable")).Value = block.gameObject;
                        continue;
                    }
                    var reference = reader.FsmVariables.FindFsmGameObject((string)Get(entry, "TargetVariable"));
                    if (Get(entry, "CoolingAmbientSource") != null) { reference.Value = EnsureSavedCoolingAmbient().gameObject; continue; }
                    if (Get(entry, "CoolingAirflowSource") != null) { reference.Value = EnsureSavedAirflow((byte)Get(Get(entry, "CoolingAirflowSource"), "Index")).gameObject; continue; }
                    if (Get(entry, "CoolantHoseSource") != null) { reference.Value = EnsureSavedCoolantHose((byte)Get(Get(entry, "CoolantHoseSource"), "Index")).gameObject; continue; }
                    if (Get(entry, "RadiatorSource") != null) { reference.Value = EnsureSavedRadiator().gameObject; continue; }
                    if (Get(entry, "RockerCoverSource") != null) { reference.Value = EnsureSavedRockerCover().gameObject; continue; }
                    if (Get(entry, "OilpanSource") != null) { reference.Value = EnsureSavedOilpan().gameObject; continue; }
                    if (Get(entry, "ExhaustSource") != null) { reference.Value = EnsureSavedExhaust(Get(entry, "ExhaustSource")).gameObject; continue; }
                    if (Get(entry, "AirCleanerSource") != null) { reference.Value = EnsureSavedAirCleaner().gameObject; continue; }
                    if (Get(entry, "CarburettorSource") != null) { reference.Value = EnsureSavedCarburettor().gameObject; continue; }
                    if (Get(entry, "HeadSource") != null) { reference.Value = EnsureSavedCylinderHead().gameObject; continue; }
                    if (Get(entry, "GearboxSource") != null) { reference.Value = EnsureSavedGearbox().gameObject; continue; }
                    if (Get(entry, "BatterySource") != null) { reference.Value = EnsureSavedBattery().gameObject; continue; }
                    var wire = Get(entry, "WiringSource");
                    if (wire != null) { ConfigureWireSource(wire, reference); continue; }
                    if ((byte)Get(entry, "SlotIndex") != 0) { ConfigureSlotSource(entry, reference); continue; }
                    if (prefix == _familyPrefix) { reference.Value = Mount.gameObject; continue; }
                    if (!_auxiliaryInputs.TryGetValue(prefix, out var source))
                    {
                        string path = (string)Get(entry, "MountPath");
                        source = new AuxiliaryInput {
                            Mount = Data(MountObject(path), null, 0),
                            Original = Data(Child(Extras, "saved additional part " + prefix), prefix + "0", 1) };
                        Seed(source.Mount, 77, 8, 2, true); Seed(source.Original, 77, 8, 2, true);
                        source.Mount.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", source.Original.gameObject) };
                        foreach (object family in (IEnumerable)Get(entry, "Families"))
                        {
                            var variant = AddVariant((string)Get(family, "Prefix"), source.Mount); source.Variants.Add(variant);
                            NativeBagPartChecks.Start(variant.Part); var factory = (PlayMakerFSM)Get(variant.Factory, "Fsm");
                            NativeBagPartChecks.Start(factory); Call(Get(variant.Factory, "Suppressor"), "Suppress", factory);
                        }
                        NativeBagPartChecks.Start(source.Mount); NativeBagPartChecks.Start(source.Original);
                        _auxiliaryInputs.Add(prefix, source);
                    }
                    reference.Value = source.Mount.gameObject;
                }
            }
            private void Load(Type catalog, PlayMakerFSM reader, Dictionary<string, object> nativeRow)
            {
                ConfigureSources(reader);
                var profile = catalog.GetProperty("GuestEngineProtection", Static).GetValue(null, null); object? writer = null;
                foreach (object candidate in (IEnumerable)Get(profile, "Writers")) if ((string)Get(candidate, "Fsm") == reader.FsmName) writer = candidate;
                Require(writer != null, "Native protection rule missing.");
                bool oil = reader.FsmName == "Oil", cooling = reader.FsmName == "Cooling", fuel = reader.FsmName == "FuelLine", wearing = reader.FsmName == "Wearing", valves = reader.FsmName == "Valves";
                bool electrics = reader.FsmName == "Electrics", mixture = reader.FsmName == "Mixture";
                var native = mixture ? new[] { "Pistons" } : electrics ? new[] { "Check alternator", "Alternator damage", "Run on alternator", "State 2" } : fuel ? new[] { "Fuel Pump", "Not Ok 4" } : wearing || valves ? new string[0] : oil ? (_alternator ? new[] { "Water Pump", "Oil pump?", "Alternator" } : new[] { "Water Pump", "Oil pump?" }) : cooling ? new[] { "Water Pump 2", "Pump tightness", "Open", "Closed" } : _starter ? new[] { "Wiring", "Starter damage" }
                    : new[] { "Ignition", "Not Ok", "Spark angle?", "Damage?", "Distributor tight?", "Random move", "Cam wear" };
                if (_fluids)
                {
                    if (cooling) native = new[] { "Thermostat", "Housing tightness" };
                    else
                    {
                        var states = new List<string>(native);
                        if (oil) { states.Add("Headgasket"); states.Add("Oilfilter leak"); states.Add("Oil filter"); }
                        else { states.Add("Head gasket"); states.Add("Gasket damage"); }
                        native = states.ToArray();
                    }
                }
                if (_powertrain)
                {
                    var states = new List<string>(native);
                    if (reader.FsmName == "Cylinders") { states.Add("Powertrain"); states.Add("Crank"); }
                    if (oil) states.Add("Crank wear");
                    native = states.ToArray();
                }
                if (_timingbelt)
                {
                    var states = new List<string>(native) { "Powertrain", "Timing belt" }; native = states.ToArray();
                }
                if (_fan)
                {
                    var states = new List<string>(native);
                    if (cooling) states.Add("Fan");
                    if (valves) { states.Add("Fan belt"); states.Add("Radiator fan"); }
                    native = states.ToArray();
                }
                if (_fanbelt)
                {
                    var states = new List<string>(native); states.Add("Fan belt"); if (cooling) states.Add("Fan");
                    // Keep Fan's completion boundary: the next native state drives
                    // other cooling systems outside this isolated belt check.
                    native = states.ToArray();
                }
                foreach (Dictionary<string, object> row in (IEnumerable)nativeRow["states"])
                {
                    string name = (string)row["name"]; var state = NativeBagPartChecks.State(reader, name); var raw = (List<object>)row["actions"];
                    var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < raw.Count; i++)
                    {
                        bool selected = (_fluids && cooling && (name == "Closed" || name == "Open")) || (_powertrain && ((wearing && name == "Pressure leak" && i <= 15) || (fuel && name == "Fuel Usage" && i == 17))) || (_timingbelt && name == "Timing belt wear" && (i == 0 || i == 2)) || (_fanbelt && ((oil && name == "No belt") || (cooling && name == "Fan"))) || Array.IndexOf(native, name) >= 0 || (_starter && name == "Fuel Mixture" && i == 10)
                            || (oil && ((name == "Starting engine" && i == 4) || (name == "Wearing 2" && i != 1)))
                            || (_alternator && oil && (name == "Alternator seize" || (name == "Wearing 3" && i == 1) || (name == "Wearing 3" && i == 2)))
                            || (cooling && name == "State 2" && i < 3)
                            || (fuel && ((name == "State 2" && i == 1) || (name == "Fuel Usage" && (i == 8 || i == 10 || i == 11 || i == 14 || i == 15))))
                            || (wearing && ((name == "State 4" && i <= 2) || (name == "Durability 2" && (i == 0 || i == 2)) || (name == "Durability 1" && i == 0)))
                            || (reader.FsmName == "Cylinders" && ((name == "Powertrain" && i >= 3) || (name == "Break 2" && i >= 1)))
                            || (valves && ((name == "Get cam profile" && i < 8) || (name == "Cyl 1 intake" && i >= 1) || (name == "Cyl1 power 9" && i != 1)));
                        if (valves && (name == "Get cam profile" && i == 8 || name.StartsWith("Cyl ", StringComparison.Ordinal) && i == 0)) selected = true;
                        if (electrics) selected = ElectricalNativeAction(name, i);
                        if (_fluids && oil && name == "Oilfilter leak" && i == 3) selected = false;
                        var inputs = catalog.GetProperty("GuestEngineInputs", Static).GetValue(null, null);
                        foreach (object entry in (IEnumerable)Get(inputs, "Entries"))
                            if ((string)Get(entry, "Fsm") == reader.FsmName)
                                foreach (object read in (IEnumerable)Get(entry, "Readers"))
                                    if ((string)Get(read, "State") == name && (int)Get(read, "ActionIndex") == i) selected = true;
                        foreach (object rule in (IEnumerable)Get(writer!, "Actions")) if ((string)Get(rule, "State") == name && (int)Get(rule, "Index") == i) selected = true;
                        foreach (object rule in (IEnumerable)Get(writer!, "PoseActions")) if ((string)Get(rule, "State") == name && (int)Get(rule, "Index") == i) selected = true;
                        actions[i] = selected ? NativeAction((Dictionary<string, object>)raw[i], reader) : new Quiet();
                    }
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    if (Array.IndexOf(native, name) < 0 && !(fuel && name == "Fuel Usage")
                        && !(reader.FsmName == "Cylinders" && (name == "Powertrain" || name == "Break 2"))
                        && !(valves && name == "Cyl 1 intake")) state.Transitions = new FsmTransition[0];
                }
                ConfigureDirectBlockTargets(reader);
                if (valves) ConfigureValveSources(reader);
                if (electrics) ((FsmOwnerDefault)Get(NativeBagPartChecks.State(reader, "Engine off").Actions[1], "gameObject")).GameObject.Value = Starter.gameObject;
                if (fuel) ((FsmOwnerDefault)Get(NativeBagPartChecks.State(reader, "Not Ok 4").Actions[3], "gameObject")).GameObject.Value = Starter.gameObject;
                if (_starter || oil || cooling || fuel || wearing || valves || electrics || mixture) return;
                ((FsmOwnerDefault)Get(NativeBagPartChecks.State(reader, "Not Ok").Actions[1], "gameObject")).GameObject.Value = Starter.gameObject;
                ((FsmOwnerDefault)Get(NativeBagPartChecks.State(reader, "Spark angle?").Actions[0], "gameObject")).GameObject.Value = Child(Extras, "ping sound target");
                ((FsmOwnerDefault)Get(NativeBagPartChecks.State(reader, "Spark angle?").Actions[2], "gameObject")).GameObject.Value = Boost.gameObject;
            }
            public void Dispose()
            {
                Call(Sync, "RestoreGuestEngineInputs"); Property(Session, "State", SessionState.Idle); Set(_world, "_syncReady", false);
                RestoreSlotDatabase();
                UnityEngine.Object.DestroyImmediate(Car); UnityEngine.Object.DestroyImmediate(Extras); UnityEngine.Object.DestroyImmediate(Controller);
                StaticProperty(typeof(SessionManager), "Instance", _savedSession); StaticProperty(World, "Instance", _savedWorld);
            }
        }

        private static PlayMakerFSM Data(GameObject obj, string? id, int assembly)
        {
            // Auxiliary parts can be created after the fixture is active. Defer
            // Awake until the replacement FSM and its identity variables exist.
            bool active = obj.activeSelf; obj.SetActive(false);
            var fsm = Empty(obj, "Data");
            fsm.FsmVariables.StringVariables = new[] { new FsmString { Name = "ID", UseVariable = true, Value = id ?? "" },
                new FsmString { Name = "UTAssemblyID", UseVariable = true, Value = id == null ? "" : id + "AID" },
                new FsmString { Name = "UTPos", UseVariable = true, Value = id == null ? "" : id + "POS" } };
            fsm.FsmVariables.IntVariables = new[] { new FsmInt { Name = "AssemblyID", UseVariable = true, Value = assembly } };
            if (id == null) { fsm.FsmVariables.StringVariables = new FsmString[0]; fsm.FsmVariables.IntVariables = new FsmInt[0]; }
            var strings = new List<FsmString>(fsm.FsmVariables.StringVariables);
            strings.Add(new FsmString { Name = "CamProfile", UseVariable = true, Value = "51003100" });
            fsm.FsmVariables.StringVariables = strings.ToArray();
            fsm.FsmVariables.BoolVariables = new[] { new FsmBool { Name = "Consumed", UseVariable = true }, new FsmBool { Name = "Installed", UseVariable = true, Value = assembly > 0 } };
            fsm.FsmVariables.FloatVariables = new[] { new FsmFloat { Name = "Wear", UseVariable = true }, new FsmFloat { Name = "Tightness", UseVariable = true },
                new FsmFloat { Name = "InertiaFactor", UseVariable = true, Value = .2f },
                new FsmFloat { Name = "SettingRPM", UseVariable = true, Value = 6200f },
                new FsmFloat { Name = "Dirt", UseVariable = true, Value = 6 },
                new FsmFloat { Name = "SparkAngle", UseVariable = true }, new FsmFloat { Name = "Durability", UseVariable = true, Value = 2 },
                new FsmFloat { Name = "Efficiency", UseVariable = true, Value = 3 },
                new FsmFloat { Name = "SettingRotation", UseVariable = true, Value = 7 },
                new FsmFloat { Name = "Friction", UseVariable = true, Value = 4.2f },
                new FsmFloat { Name = "ValveTolerance", UseVariable = true, Value = 3 },
                new FsmFloat { Name = "OutputRate", UseVariable = true, Value = 400 } };
            if (id == null)
            {
                var bools = new List<FsmBool>(fsm.FsmVariables.BoolVariables); bools.Add(new FsmBool { Name = "Damaged", UseVariable = true, Value = true });
                fsm.FsmVariables.BoolVariables = bools.ToArray();
            }
            obj.SetActive(active);
            return fsm;
        }
        private static void Seed(PlayMakerFSM fsm, float wear, float tightness, float angle, bool installed)
        { fsm.FsmVariables.FindFsmFloat("Wear").Value = wear; fsm.FsmVariables.FindFsmFloat("Tightness").Value = tightness; fsm.FsmVariables.FindFsmFloat("SparkAngle").Value = angle; fsm.FsmVariables.FindFsmBool("Installed").Value = installed; }
        private static PlayMakerFSM Empty(GameObject obj, string name)
        {
            var fsm = obj.AddComponent<PlayMakerFSM>(); fsm.enabled = false; Set(fsm, "fsm", new Fsm()); fsm.Fsm.Name = name; fsm.Fsm.StartState = "Probe idle";
            fsm.Fsm.States = new[] { new FsmState(fsm.Fsm) { Name = "Probe idle", Actions = new FsmStateAction[0], Transitions = new FsmTransition[0] } }; return fsm;
        }
        private sealed class Quiet : FsmStateAction { public override void OnEnter() { Finish(); } }
        private static FsmStateAction NativeAction(Dictionary<string, object> row, PlayMakerFSM fsm) => (FsmStateAction)typeof(NativeBagPartChecks).GetMethod("ReadAction", Static).Invoke(null, new object[] { row, fsm });
        private static FsmGameObject ObjectVar(string name, GameObject value) => new FsmGameObject { Name = name, UseVariable = true, Value = value };
        private static GameObject Child(GameObject parent, string name) { var obj = new GameObject(name); obj.transform.SetParent(parent.transform, false); return obj; }
        private static GameObject PathObject(GameObject root, string path) { var obj = root; var parts = path.Split('/'); for (int i = 1; i < parts.Length; i++) { var found = obj.transform.Find(parts[i]); obj = found == null ? Child(obj, parts[i]) : found.gameObject; } return obj; }
        private static object Nested(string name) => Activator.CreateInstance(Items.GetNestedType(name, BindingFlags.NonPublic), true);
        private static object Get(object target, string name) => (target.GetType().GetField(name, Members) ?? throw new MissingFieldException(target.GetType().Name, name)).GetValue(target);
        private static void Set(object target, string name, object? value) => (target.GetType().GetField(name, Members) ?? throw new MissingFieldException(target.GetType().Name, name)).SetValue(target, value);
        private static void Property(object target, string name, object? value) => target.GetType().GetProperty(name, Members).SetValue(target, value, null);
        private static void StaticProperty(Type type, string name, object? value) => type.GetProperty(name, Static).SetValue(null, value, null);
        private static object? Call(object target, string name, params object?[] values) => target.GetType().GetMethod(name, Members).Invoke(target, values);
        private static bool Near(float a, float b) => Math.Abs(a - b) < .0001f;
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
