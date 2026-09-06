using System;
using System.Collections;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Catalog;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        private PlayMakerFSM? GetPartSlotMount(ReplacementBinding part, out byte slot)
        {
            slot = 0;
            var c = SyncCatalog.ReplacementParts!;
            try
            {
                var database = FsmVariables.GlobalVariables.FindFsmGameObject(c["slotDatabaseVariable"])?.Value;
                if (database == null || !database.activeInHierarchy) return null;
                RequireFit(ScenePath.Of(database.transform) == c["slotDatabasePath"]
                    && part.Data.FsmVariables.FindFsmString(c["slotReferenceVariable"])?.Value == part.Factory.Rule.SlotReference);
                if (part.SlotInstaller == null || part.SlotInstaller.gameObject != database)
                {
                    PlayMakerFSM? installer = null;
                    foreach (var fsm in database.GetComponents<PlayMakerFSM>())
                        if (fsm.FsmName == c["slotInstallerFsm"]) { RequireFit(installer == null); installer = fsm; }
                    if (installer == null || !FitFsmReady(installer)) return null;
                    ValidatePartSlotInstaller(installer, c);
                    part.SlotInstaller = installer;
                }
                if (!FitFsmReady(part.SlotInstaller)) return null;
                IList? points = null;
                foreach (var component in database.GetComponents<MonoBehaviour>())
                {
                    if (component == null || component.GetType().Name != "PlayMakerArrayListProxy"
                        || component.GetType().GetField("referenceName")?.GetValue(component) as string != part.Factory.Rule.SlotReference) continue;
                    RequireFit(points == null);
                    points = component.GetType().GetProperty("arrayList")?.GetValue(component, null) as IList;
                    RequireFit(points != null);
                }
                if (points == null || points.Count == 0) return null;
                RequireFit(points.Count == part.Factory.Rule.SlotCount + 1 && points[0] == null);
                var positions = new NetVector3?[points.Count];
                var seen = new HashSet<GameObject>();
                for (int i = 1; i < points.Count; i++)
                {
                    var point = points[i] as GameObject;
                    if (point == null) { RequireFit(points[i] == null || points[i] is GameObject); continue; }
                    RequireFit(seen.Add(point));
                    // Native selection includes occupied and inactive slots. Do
                    // not silently choose a farther free slot instead.
                    positions[i] = point.transform.position.ToNet();
                }
                slot = PartSlotPolicy.Nearest(part.Data.transform.position.ToNet(), positions, SlotTolerance(part.SlotInstaller));
                if (slot == 0) return null;
                var selected = (GameObject)points[slot];
                if (!selected.activeInHierarchy) return null;
                if (part.FitMount != null && part.FitMount.gameObject == selected) return part.FitMount;
                foreach (var mount in selected.GetComponents<PlayMakerFSM>())
                {
                    if (mount.FsmName != c["itemFsm"] || !PartFitMountReady(mount)) continue;
                    ValidatePartFitMount(mount, c, true);
                    RequireFit(mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value == selected);
                    part.FitMount = mount;
                    return mount;
                }
            }
            catch (Exception e) { DisablePartFit(part, e.Message); }
            return null;
        }

        private static float SlotTolerance(PlayMakerFSM installer)
        {
            var state = FsmHook.FindState(installer, SyncCatalog.ReplacementParts!["fitFarState"]);
            if (state != null)
                foreach (var action in state.Actions)
                    if (action.GetType().Name == "FloatCompare") return PackageField<FsmFloat>(action, "float2")?.Value ?? float.NaN;
            return float.NaN;
        }

        private static bool SlotInstallerIdle(ReplacementBinding part)
        {
            if (part.Factory.Rule.SlotCount == 0) return true;
            var c = SyncCatalog.ReplacementParts!;
            return part.SlotInstaller != null && FitFsmReady(part.SlotInstaller)
                && part.SlotInstaller.ActiveStateName == c["slotInstallerIdleState"]
                && part.SlotInstaller.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value == null
                && part.SlotInstaller.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value == null
                && part.SlotInstaller.FsmVariables.FindFsmInt(c["slotInstallerIndexVariable"])?.Value == 0;
        }

        private void GuardPartSlotSelection(SessionManager session, PartFitting fitting)
        {
            if (fitting.Request.SlotIndex == 0) return;
            var installer = fitting.Part.SlotInstaller!;
            fitting.SelectionState = FsmHook.FindState(installer, SyncCatalog.ReplacementParts!["fitNearState"]);
            fitting.SelectionGuard = new FsmHookAction(() =>
            {
                if (_partFitting != fitting || fitting.Committed) return;
                try
                {
                    if (!PartSlotSelectionMatches(fitting)) throw new InvalidOperationException("Native slot selection changed.");
                    var state = BuildReplacementPartState(fitting.Request.ItemId);
                    if (state == null || state.Revision != fitting.Request.ExpectedRevision || state.Installed
                        || state.AssemblyId != 0 || Time.unscaledTime >= fitting.Deadline
                        || !GuestOwnsFitPart(fitting.Request.ItemId, fitting.Request.PlayerId)
                        || !GuestNearPackage(session, fitting.Request.PlayerId, fitting.Part.Data.transform.position)
                        || !GuestNearPackage(session, fitting.Request.PlayerId, fitting.Mount.transform.position))
                        throw new InvalidOperationException("Slot candidate changed before native mount checks.");
                }
                catch (Exception e) { FailPartFitting(session, e.Message); }
            });
            var actions = new List<FsmStateAction>(fitting.SelectionState!.Actions);
            actions.Insert(0, fitting.SelectionGuard); fitting.SelectionState.Actions = actions.ToArray();
        }

        private bool PartSlotSelectionMatches(PartFitting fitting)
        {
            var c = SyncCatalog.ReplacementParts!;
            var installer = fitting.Part.SlotInstaller;
            if (installer == null || !FitFsmReady(installer) || !FitFsmReady(fitting.Mount)
                || installer.ActiveStateName != c["fitNearState"]
                || installer.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != fitting.Part.Data.gameObject
                || installer.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != fitting.Mount.gameObject
                || installer.FsmVariables.FindFsmString(c["slotInstallerReferenceVariable"])?.Value != fitting.Part.Factory.Rule.SlotReference
                || installer.FsmVariables.FindFsmInt(c["slotInstallerIndexVariable"])?.Value != fitting.Request.SlotIndex
                || fitting.Mount.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value != fitting.Mount.gameObject
                || fitting.Mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value != false) return false;
            return GetPartFitMount(fitting.Part, out byte slot) == fitting.Mount && slot == fitting.Request.SlotIndex;
        }

        private bool PartSlotCandidateMatches(PartFitting fitting)
        {
            var c = SyncCatalog.ReplacementParts!;
            return PartSlotSelectionMatches(fitting)
                && fitting.Mount.FsmVariables.FindFsmBool(c["slotAllowVariable"])?.Value == true
                && fitting.Mount.FsmVariables.FindFsmInt(c["assemblyVariable"])?.Value == fitting.Request.SlotIndex;
        }

        private static void CancelPartSlotPreview(PartFitting fitting)
        {
            if (fitting.Committed) return;
            var c = SyncCatalog.ReplacementParts!;
            var installer = fitting.Part.SlotInstaller;
            if (installer == null || !FitFsmReady(installer)
                || installer.FsmVariables.FindFsmGameObject(c["mountPartVariable"])?.Value != fitting.Part.Data.gameObject) return;
            if (installer.FsmVariables.FindFsmGameObject(c["mountPointVariable"])?.Value == fitting.Mount.gameObject
                && FitFsmReady(fitting.Mount) && fitting.Mount.FsmVariables.FindFsmBool(c["mountInstalledVariable"])?.Value == false)
            {
                fitting.Mount.FsmVariables.FindFsmBool(c["slotAllowVariable"]).Value = false;
                if (fitting.Mount.ActiveStateName == c["fitFarState"] || fitting.Mount.ActiveStateName == c["fitNearState"])
                    fitting.Mount.SendEvent(c["fitCancelEvent"]);
            }
            installer.SendEvent(c["slotStopEvent"]);
        }

    }
}
