using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private sealed class GuestSlotProxyAccess
        {
            internal readonly FieldInfo? ReferenceName;
            internal readonly PropertyInfo? ArrayList;

            internal GuestSlotProxyAccess(Type type)
            {
                ReferenceName = type.GetField("referenceName");
                ArrayList = type.GetProperty("arrayList");
            }
        }

        private static readonly Dictionary<Type, GuestSlotProxyAccess?> GuestSlotProxyTypes
            = new Dictionary<Type, GuestSlotProxyAccess?>();

        private static GuestSlotProxyAccess? GuestSlotAccess(Type type)
        {
            if (!GuestSlotProxyTypes.TryGetValue(type, out var access))
            {
                // CLR type metadata is immutable. Component membership, field
                // contents and slot arrays are deliberately read again below.
                access = type.Name == "PlayMakerArrayListProxy" && type.Assembly.GetName().Name == "Assembly-CSharp"
                    ? new GuestSlotProxyAccess(type) : null;
                GuestSlotProxyTypes.Add(type, access);
            }
            return access;
        }

        private static GameObject GuestEngineSlotMount(GuestEngineInputData rule, ReplacementPartFactoryData family)
        {
            var c = SyncCatalog.ReplacementParts!;
            var database = FsmVariables.GlobalVariables.FindFsmGameObject(c["slotDatabaseVariable"])?.Value;
            if (database == null || !database.activeInHierarchy || ScenePath.Of(database.transform) != c["slotDatabasePath"])
                throw new InvalidOperationException("Native engine slot database is unavailable.");
            IList? slots = null;
            foreach (var component in database.GetComponents<MonoBehaviour>())
            {
                if (component == null) continue;
                var access = GuestSlotAccess(component.GetType());
                if (access == null || access.ReferenceName?.GetValue(component) as string != family.SlotReference) continue;
                if (slots != null) throw new InvalidOperationException("Ambiguous native engine slot array.");
                slots = access.ArrayList?.GetValue(component, null) as IList;
                if (slots == null) throw new InvalidOperationException("Missing native engine slot array.");
            }
            if (slots == null || slots.Count != family.SlotCount + 1 || slots[0] != null)
                throw new InvalidOperationException("Native engine slot array shape changed.");
            var seen = new HashSet<GameObject>();
            for (int i = 1; i < slots.Count; i++)
                if (slots[i] is not GameObject point || point == null || !seen.Add(point))
                    throw new InvalidOperationException("Missing or duplicate native engine slot.");
            return (GameObject)slots[rule.SlotIndex];
        }

        private static void ValidateGuestEngineSlotTemplate(ReplacementFactory factory, GuestEngineInputData rule)
        {
            var data = factory.TemplateData;
            var c = SyncCatalog.ReplacementParts!;
            if (data == null || data.FsmName != c["itemFsm"]
                || data.FsmVariables.FindFsmString(c["slotReferenceVariable"])?.Value != factory.Rule.SlotReference
                || data.FsmVariables.FindFsmInt(c["assemblyVariable"]) == null)
                throw new InvalidOperationException("Native engine slot family changed.");
            foreach (var read in rule.Readers)
            {
                // Slotted Data has AssemblyID, not Installed. Installation comes
                // from the accepted attachment and matching applied slot identity.
                if (read.Variable == "Installed") continue;
                if (read.Variable == "Bolted") ValidateGuestEngineRockerTemplate(factory);
                else if (data.FsmVariables.FindFsmFloat(read.Variable) == null)
                    throw new InvalidOperationException("Native engine slot scalar is missing.");
            }
        }

        private static void ValidateGuestEngineRockerTemplate(ReplacementFactory factory)
        {
            var data = factory.TemplateData;
            var c = SyncCatalog.ReplacementParts!;
            if (data == null || data.FsmVariables.FindFsmString(c["slotReferenceVariable"])?.Value != factory.Rule.SlotReference)
                throw new InvalidOperationException("Native rocker slot family changed.");
            var tightness = data.FsmVariables.FindFsmFloat(c["removeTightnessVariable"]);
            var install = data.FsmVariables.FindFsmGameObject(c["installPointVariable"]);
            if (tightness == null || install == null) throw new InvalidOperationException("Native rocker inputs are incomplete.");
            var tight = GuestEngineInputState(data, c["removeTightnessState"]);
            var compare = RockerAction(tight, 1, "FloatCompare");
            RequireFit(ReferenceEquals(PackageField<FsmFloat>(compare, "float1"), tightness));
            HandScrewCompare(compare, tightness.Name, 1, "PROCEED", "BACK", "PROCEED"); HandScrewFrame(compare, false);
            RequireFitTransition(data, tight.Name, "PROCEED", c["removeBoltedState"]);
            RequireFitTransition(data, tight.Name, "BACK", c["removeUnboltedState"]);
            foreach (string name in new[] { c["removeBoltedState"], c["removeUnboltedState"] })
            {
                var action = RockerAction(GuestEngineInputState(data, name), 0, "SetFsmBool");
                var owner = PackageField<FsmOwnerDefault>(action, "gameObject");
                RequireFit(owner != null && owner.OwnerOption == OwnerDefaultOption.SpecifyGameObject
                    && ReferenceEquals(owner.GameObject, install) && install.UseVariable);
                HandScrewScalarTarget(action, data.FsmName, "Bolted"); HandScrewFrame(action, false);
                RequireFit(PackageField<FsmBool>(action, "setValue")?.UseVariable == false
                    && PackageField<FsmBool>(action, "setValue")?.Value == (name == c["removeBoltedState"]));
            }
        }

        private static FsmStateAction RockerAction(FsmState state, int index, string type)
        {
            var action = FsmHook.NativeAction(state, index)
                ?? throw new InvalidOperationException("Native rocker action is unavailable.");
            if (!action.Enabled || action.GetType().FullName != "HutongGames.PlayMaker.Actions." + type
                || action.GetType().Assembly.GetName().Name != "Assembly-CSharp")
                throw new InvalidOperationException("Native rocker action changed.");
            return action;
        }
    }
}
