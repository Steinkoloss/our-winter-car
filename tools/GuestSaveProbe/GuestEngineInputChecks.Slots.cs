using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.GuestSaveProbe
{
    internal static partial class GuestEngineInputChecks
    {
        private sealed partial class Fixture
        {
            internal readonly Variant[] Rockers = new Variant[8];
            internal readonly Variant[] Bearings = new Variant[5];
            internal readonly Variant[] Pistons = new Variant[4];
            internal readonly Variant[] Sparkplugs = new Variant[4];
            internal GameObject SlotDatabase = null!;
            internal IList SlotArray = null!, BearingSlotArray = null!, PistonSlotArray = null!, SparkplugSlotArray = null!;
            internal PlayMakerFSM RockerTemplate = null!, RockerOutput = null!;
            internal PlayMakerFSM BearingTemplate = null!, PistonTemplate = null!, SparkplugTemplate = null!;
            private FsmGameObject[]? _savedGlobalObjects;
            private FsmGameObject? _databaseReference;
            private GameObject? _savedDatabase;

            private void ConfigureSlotSource(object entry, FsmGameObject reference)
            {
                byte slot = (byte)Get(entry, "SlotIndex");
                string prefix = (string)Get(entry, "FamilyPrefix"); bool rocker = prefix == "VIN117";
                bool bearing = prefix == "VIN104", plug = prefix == "SPRKPLUG0";
                string arrayName = rocker ? "Rockers" : bearing ? "MainBearings" : plug ? "Sparkplugs" : "Pistons";
                var variants = rocker ? Rockers : bearing ? Bearings : plug ? Sparkplugs : Pistons;
                if (SlotDatabase == null)
                {
                    SlotDatabase = PathObject(Car, "CORRIS/AssembyDatabase");
                    var globals = FsmVariables.GlobalVariables; _savedGlobalObjects = globals.GameObjectVariables;
                    _databaseReference = globals.FindFsmGameObject("AssemblyDatabase");
                    if (_databaseReference == null)
                    {
                        _databaseReference = new FsmGameObject { Name = "AssemblyDatabase", UseVariable = true };
                        var values = new List<FsmGameObject>(globals.GameObjectVariables) { _databaseReference }; globals.GameObjectVariables = values.ToArray();
                    }
                    _savedDatabase = _databaseReference.Value; _databaseReference.Value = SlotDatabase;
                }
                var slots = rocker ? SlotArray : bearing ? BearingSlotArray : plug ? SparkplugSlotArray : PistonSlotArray;
                if (slots == null)
                {
                    Type? arrayType = null;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        if (assembly.GetName().Name == "Assembly-CSharp") arrayType = assembly.GetType("PlayMakerArrayListProxy");
                    Require(arrayType != null, "Missing native slot array component.");
                    var proxy = SlotDatabase.AddComponent(arrayType); Set(proxy, "referenceName", arrayName);
                    slots = (IList)arrayType!.GetProperty("arrayList").GetValue(proxy, null); slots.Clear();
                    for (int i = 0; i <= variants.Length; i++) slots.Add(null);
                    if (rocker) SlotArray = slots; else if (bearing) BearingSlotArray = slots; else if (plug) SparkplugSlotArray = slots; else PistonSlotArray = slots;
                }
                if (variants[slot - 1] == null)
                {
                    var mount = Data(MountObject((string)Get(entry, "MountPath")), null, 0); Seed(mount, 77, 8, 2, true);
                    var bools = new List<FsmBool>(mount.FsmVariables.BoolVariables) { new FsmBool { Name = "Bolted", UseVariable = true, Value = true } };
                    mount.FsmVariables.BoolVariables = bools.ToArray();
                    var original = Data(Child(Extras, "saved " + arrayName + " " + slot), prefix + (500 + slot), slot); Seed(original, 77, 8, 2, true);
                    mount.FsmVariables.GameObjectVariables = new[] { ObjectVar("ActivePart", original.gameObject) };
                    var variant = AddVariant(prefix, mount, prefix + (100 + slot)); variants[slot - 1] = variant;
                    var strings = new List<FsmString>(variant.Part.FsmVariables.StringVariables) {
                        new FsmString { Name = "ArrayReference", UseVariable = true, Value = arrayName } };
                    variant.Part.FsmVariables.StringVariables = strings.ToArray();
                    // The actual slotted prefab has no Installed scratch variable.
                    variant.Part.FsmVariables.BoolVariables = new[] { variant.Part.FsmVariables.FindFsmBool("Consumed") };
                    variant.Part.FsmVariables.IntVariables[0].Value = slot;
                    slots[slot] = mount.gameObject;
                    if (rocker && RockerTemplate == null) CreateRockerTemplate(variant.Factory);
                    if (bearing && BearingTemplate == null) BearingTemplate = CreateSlotTemplate(variant.Factory, prefix, "bearing-engine-input-probe.json");
                    if (plug && SparkplugTemplate == null) SparkplugTemplate = CreateSlotTemplate(variant.Factory, prefix, "sparkplug-engine-input-probe.json");
                    if (!rocker && !bearing && !plug && PistonTemplate == null) PistonTemplate = CreateSlotTemplate(variant.Factory, prefix, "piston-engine-input-probe.json");
                    NativeBagPartChecks.Start(variant.Part); NativeBagPartChecks.Start(mount); NativeBagPartChecks.Start(original);
                    var factory = (PlayMakerFSM)Get(variant.Factory, "Fsm");
                    if (!factory.Fsm.Started) NativeBagPartChecks.Start(factory);
                    Call(Get(variant.Factory, "Suppressor"), "Suppress", factory);
                    var source = new AuxiliaryInput { Mount = mount, Original = original }; source.Variants.Add(variant);
                    _auxiliaryInputs.Add(prefix + "::" + slot, source);
                }
                reference.Value = variants[slot - 1].Mount.gameObject;
            }

            private PlayMakerFSM CreateSlotTemplate(object factory, string prefix, string fixture)
            {
                var type = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var json = Activator.CreateInstance(type, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../" + fixture)) }, null);
                var rows = (List<object>)((Dictionary<string, object>)type.GetMethod("ReadObject", Members).Invoke(json, null))["fsms"];
                var template = NativeBagPartChecks.MakeFsm(Child(Extras, "native " + prefix + " template"), NativeBagPartChecks.Find(rows, prefix, "Data"));
                Set(factory, "TemplateData", template); Set(factory, "Prefab", template.gameObject);
                ((PlayMakerFSM)Get(factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP").Value = null;
                NativeBagPartChecks.Start(template); return template;
            }

            private void CreateRockerTemplate(object factory)
            {
                var type = Core.GetType("WinterMP.Core.Catalog.SyncCatalogJson+Reader", true);
                var json = Activator.CreateInstance(type, Members, null, new object[] { File.ReadAllText(Path.Combine(Application.dataPath, "../rocker-engine-input-probe.json")) }, null);
                var rows = (List<object>)((Dictionary<string, object>)type.GetMethod("ReadObject", Members).Invoke(json, null))["fsms"];
                var row = NativeBagPartChecks.Find(rows, "VIN117", "Data");
                RockerTemplate = NativeBagPartChecks.MakeFsm(Child(Extras, "native rocker template"), row);
                RockerOutput = Data(Child(Extras, "native rocker output"), null, 0);
                var booleans = new List<FsmBool>(RockerOutput.FsmVariables.BoolVariables) { new FsmBool { Name = "Bolted", UseVariable = true } };
                RockerOutput.FsmVariables.BoolVariables = booleans.ToArray();
                RockerTemplate.FsmVariables.FindFsmGameObject("InstallPoint").Value = RockerOutput.gameObject;
                foreach (Dictionary<string, object> stateRow in (IEnumerable)row["states"])
                {
                    string name = (string)stateRow["name"]; var state = NativeBagPartChecks.State(RockerTemplate, name);
                    var raw = (List<object>)stateRow["actions"]; var actions = new FsmStateAction[raw.Count];
                    for (int i = 0; i < actions.Length; i++) actions[i] = name == "Tightness?" || ((name == "Bolted" || name == "Unbolted") && i == 0)
                        ? NativeAction((Dictionary<string, object>)raw[i], RockerTemplate) : new Quiet();
                    state.Actions = actions; foreach (var action in actions) action.Init(state);
                    if (name != "Tightness?") state.Transitions = new FsmTransition[0];
                }
                Set(factory, "TemplateData", RockerTemplate); Set(factory, "Prefab", RockerTemplate.gameObject);
                ((PlayMakerFSM)Get(factory, "Fsm")).FsmVariables.FindFsmGameObject("VINP").Value = null;
                NativeBagPartChecks.Start(RockerTemplate); NativeBagPartChecks.Start(RockerOutput);
            }

            internal ReplacementPartState ReceiveRocker(int slot, float tightness, bool applied = true, bool fitted = true,
                int? assembly = null, int? mountSlot = null)
                => ReceiveSlot(Rockers, slot, 90, tightness, applied, fitted, assembly, mountSlot);

            internal ReplacementPartState ReceiveBearing(int slot, float wear, bool applied = true, bool fitted = true,
                int? assembly = null, int? mountSlot = null)
                => ReceiveSlot(Bearings, slot, wear, 8, applied, fitted, assembly, mountSlot);

            internal ReplacementPartState ReceivePiston(int slot, float wear, bool applied = true, bool fitted = true,
                int? assembly = null, int? mountSlot = null)
                => ReceiveSlot(Pistons, slot, wear, 8, applied, fitted, assembly, mountSlot);

            internal ReplacementPartState ReceiveSparkplug(int slot, float wear, float tightness = 8, float durability = .75f,
                bool applied = true, bool fitted = true, int? assembly = null, int? mountSlot = null)
                => ReceiveSlot(Sparkplugs, slot, wear, tightness, applied, fitted, assembly, mountSlot, durability);

            private ReplacementPartState ReceiveSlot(Variant[] slots, int slot, float wear, float tightness, bool applied, bool fitted,
                int? assembly, int? mountSlot, float? durability = null)
            {
                var variant = slots[slot - 1];
                MountAddress(slots[(mountSlot ?? slot) - 1].Mount, out uint parentId, out string parentPath);
                var state = new ReplacementPartState { FactoryId = variant.FactoryId, NativeId = variant.NativeId, Revision = ++variant.Revision,
                    Scalars = durability.HasValue ? new[] { wear, tightness, durability.Value } : new[] { wear, tightness }, Rotation = NetQuaternion.Identity, LocalRotation = NetQuaternion.Identity,
                    LocalScale = new NetVector3(1, 1, 1), AssemblyId = fitted ? assembly ?? slot : 0, Installed = fitted,
                    ParentKind = fitted ? PartParentKind.NativePart : PartParentKind.None, ParentId = fitted ? parentId : 0,
                    ParentPath = fitted ? parentPath : string.Empty };
                Call(Sync, "OnReplacementPartState", state);
                Require(((ReplacementPartReplica)Get(Sync, "_replacementReplica")).Get(variant.PartId)?.Revision == state.Revision, "Slotted part state was rejected.");
                if (applied)
                {
                    variant.Part.FsmVariables.IntVariables[0].Value = state.AssemblyId;
                    ((HashSet<uint>)Get(Sync, "_pendingReplacements")).Remove(variant.PartId); Set(variant.Binding, "HasAppliedState", true);
                    Set(variant.Binding, "AppliedRevision", variant.Revision); Set(variant.Binding, "FittedPresentation", fitted);
                }
                return state;
            }

            internal FsmStateAction RockerRead(int slot) => Action("Cylinder" + ((slot + 1) / 2), slot % 2 == 1 ? 4 : 5);
            internal bool ReadRocker(int slot)
            {
                RockerRead(slot).OnEnter();
                return Reader.FsmVariables.FindFsmBool(slot % 2 == 1 ? "Installed3" : "Installed4").Value;
            }
            internal void AssertRockersSaved()
            {
                AssertSaved();
                foreach (var rocker in Rockers) Require(rocker.Mount.FsmVariables.FindFsmBool("Bolted").Value, "Rocker projection changed saved Bolted.");
            }
            internal void LoadCylinderRockerDecisions()
            {
                foreach (Dictionary<string, object> row in (IEnumerable)_row["states"])
                {
                    string name = (string)row["name"];
                    if (!name.StartsWith("Cylinder") || name.Length != 9) continue;
                    var state = NativeBagPartChecks.State(Reader, name); var raw = (List<object>)row["actions"];
                    // Keep the already-bound native piston and rocker readers.
                    for (int i = 0; i < raw.Count; i++)
                        if (i != 2 && i != 3 && i != 4 && i != 5) { state.Actions[i] = NativeAction((Dictionary<string, object>)raw[i], Reader); state.Actions[i].Init(state); }
                    var transitions = new List<FsmTransition>();
                    foreach (Dictionary<string, object> t in (IEnumerable)row["transitions"])
                        transitions.Add(new FsmTransition { FsmEvent = FsmEvent.GetFsmEvent((string)t["event"]), ToState = (string)t["to"] });
                    state.Transitions = transitions.ToArray();
                }
                for (int cylinder = 1; cylinder <= 4; cylinder++)
                {
                    Reader.FsmVariables.FindFsmFloat("Piston" + cylinder + "Wear").Value = 100;
                    Reader.FsmVariables.FindFsmFloat("Sparkplug" + cylinder + "Wear").Value = 100;
                }
            }
            private void RestoreSlotDatabase()
            {
                if (_databaseReference == null || _savedGlobalObjects == null) return;
                _databaseReference.Value = _savedDatabase; FsmVariables.GlobalVariables.GameObjectVariables = _savedGlobalObjects;
            }
        }
    }
}
