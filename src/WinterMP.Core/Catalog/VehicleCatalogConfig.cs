using System;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;
using WinterMP.Core.Sync;

namespace WinterMP.Core.Catalog
{
    internal sealed class VehicleRegistrationConfig
    {
        public float MinMass = 150f;
        public string[] NamePrefixes = { "JONNEZ", "GIFU", "JOKKIS", "CORRIS", "SORBET", "KEKMET", "BACHGLOTZ" };
        public bool RequireRoot = true;

        public bool IsVehicleRoot(Rigidbody body)
        {
            // Its three connected bodies use one hitch/physics authority. The mass
            // fallback must not also give the chassis an independent item lease.
            var trailer = SyncCatalog.TractorTrailer;
            if (trailer != null && ScenePath.Of(body.transform) == trailer["trailer"]) return false;
            // The detached native corpse weighs 450 kg. The mass fallback would
            // otherwise delegate its ragdoll and nearby meat as a pushable vehicle.
            var moose = SyncCatalog.MooseChop;
            if (moose != null && body.name == moose["detached"]) return false;

            // A body carrying the full vehicle-simulation subtree IS a drivable, even when
            // nested — the taxi (JOBS/TAXIJOB/MACHTWAGEN) is a complete sim car parented at
            // depth. Gate on the structure (Simulation/Engine) rather than a name list so it
            // generalizes to any future nested drivable. Only CORRIS/SORBET/MACHTWAGEN own
            // this subtree in the current build, so this never over-registers a part body.
            bool hasDrivableSubtree = HasDrivableSubtree(body.transform);

            // A nested rigidbody is otherwise a part/sub-assembly, not a vehicle root.
            if (RequireRoot && body.transform.parent != null && !hasDrivableSubtree) return false;
            if (hasDrivableSubtree) return true;
            if (body.mass >= MinMass) return true;

            string name = body.name;
            foreach (string prefix in NamePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasDrivableSubtree(Transform root)
        {
            try
            {
                // Transform.Find walks a slash path and reaches inactive children.
                return root.Find("Simulation/Engine") != null;
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class PickableRegistrationConfig
    {
        public string[] ExcludeNameContains = { "(VINXX)" };
        public string[] NameSuffixes = { "(itemx)", "(item2)", "(lugga)" };
        public bool ProbeUseFsm = true;

        public bool IsPickable(Rigidbody body)
        {
            string name = body.name;
            foreach (string excluded in ExcludeNameContains)
            {
                if (name.IndexOf(excluded, StringComparison.Ordinal) >= 0)
                    return false;
            }

            foreach (string suffix in NameSuffixes)
            {
                if (name.IndexOf(suffix, StringComparison.Ordinal) >= 0)
                    return true;
            }

            if (!ProbeUseFsm) return false;

            foreach (var fsm in body.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Use") continue;
                if (FsmHook.HasState(fsm, "Destroy") || FsmHook.HasState(fsm, "Destroy self"))
                    return true;
                if (FsmHook.HasState(fsm, "Check drink"))
                    return true;
            }

            return false;
        }
    }

    internal sealed class ConsumableConfig
    {
        public string FsmName = "Use";
        public string[] DestroyStates = { "Destroy", "Destroy self" };
        public string DrinkCheckState = "Check drink";
        public string[] DrinkEmptyStates = { "State 2", "State 5", "State 6" };

        public void CollectDespawnStates(PlayMakerFSM fsm, List<string> states)
        {
            if (fsm.FsmName != FsmName) return;

            foreach (string state in DestroyStates)
            {
                if (FsmHook.HasState(fsm, state) && !states.Contains(state))
                    states.Add(state);
            }

            if (!FsmHook.HasState(fsm, DrinkCheckState)) return;

            foreach (string state in DrinkEmptyStates)
            {
                if (RemovesDrinkBody(fsm, state) && !states.Contains(state))
                    states.Add(state);
            }
        }

        private static bool RemovesDrinkBody(PlayMakerFSM fsm, string name)
        {
            // Numbered states are reused by unrelated drinks: milk's State 2 is
            // its startup delay, not consumption. Require native removal of this body.
            foreach (var state in fsm.Fsm.States)
            {
                if (state.Name != name) continue;
                foreach (var action in state.Actions)
                {
                    if (!action.Enabled) continue;
                    if (action.GetType().Name == "DestroySelf") return true;
                    if (action.GetType().Name != "DestroyComponent") continue;
                    var owner = action.GetType().GetField("gameObject")?.GetValue(action) as FsmOwnerDefault;
                    var component = action.GetType().GetField("component")?.GetValue(action) as FsmString;
                    if (owner != null && (owner.OwnerOption == OwnerDefaultOption.UseOwner
                            || owner.GameObject.Value == fsm.gameObject)
                        && component != null && !component.UseVariable && component.Value == "Rigidbody") return true;
                }
                return false;
            }
            return false;
        }
    }

    internal sealed class VehicleClimateConfig
    {
        // Every vehicle with a CarTemp* cabin-climate sim in the current build. The other
        // drivables (JONNEZ/GIFU/JOKKIS/KEKMET/BACHGLOTZ) have no CarTemp/HeaterUnit
        // subtree at all — nothing to sync there, so their absence here is deliberate.
        public string[] PathPrefixes = { "SORBET(190-200psi)/", "CORRIS/", "JOBS/TAXIJOB/MACHTWAGEN/" };
        public string[] CarTempPathContains =
        {
            "/Simulation/CarTempSorbet",
            "/Simulation/CarTempCorris",
            "/Simulation/CarTempTaxi",
        };
        public string[] HeaterPathContains = { "/HeaterUnit" };

        public bool IsClimateVehiclePath(string path)
        {
            foreach (string prefix in PathPrefixes)
            {
                if (path.IndexOf(prefix, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        public bool IsCarTempRoot(string path)
        {
            foreach (string marker in CarTempPathContains)
            {
                if (path.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }

        public bool IsHeaterPath(string path)
        {
            foreach (string marker in HeaterPathContains)
            {
                if (path.IndexOf(marker, StringComparison.Ordinal) >= 0)
                    return true;
            }

            return false;
        }
    }
}
