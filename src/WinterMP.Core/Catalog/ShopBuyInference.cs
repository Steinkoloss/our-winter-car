using System.Collections.Generic;
using WinterMP.Core.Sync;

namespace WinterMP.Core.Catalog
{
    /// <summary>Infers entry guards + result states for generic Teimo / shop Buy FSMs.</summary>
    internal static class ShopBuyInference
    {
        public static bool TryInfer(PlayMakerFSM fsm, out CatalogBuyMatch? match)
        {
            match = null;
            if (fsm.FsmName != "Buy") return false;

            var guards = new List<CatalogBuyGuard>();
            if (FsmHook.HasState(fsm, "Check money"))
                guards.Add(new CatalogBuyGuard { StateName = "Check money", TriggerEvent = "USE" });
            if (FsmHook.HasState(fsm, "Check inventory"))
                guards.Add(new CatalogBuyGuard { StateName = "Check inventory", TriggerEvent = "PURCHASE" });
            if (FsmHook.HasState(fsm, "Check if 0"))
                guards.Add(new CatalogBuyGuard { StateName = "Check if 0", TriggerEvent = "DEPURCHASE" });
            if (guards.Count == 0 && FsmHook.HasState(fsm, "Purchase") && FsmHook.HasState(fsm, "Wait button"))
                guards.Add(new CatalogBuyGuard { StateName = "Purchase", TriggerEvent = "USE" });

            // Peräpörtti restaurant — PURCHASE/DEPURCHASE from Wait button, no Check money.
            if (guards.Count == 0 && FsmHook.HasState(fsm, "Cashier") && FsmHook.HasState(fsm, "Wait button")
                && !FsmHook.HasState(fsm, "Check money") && !FsmHook.HasState(fsm, "Purchase"))
            {
                guards.Add(new CatalogBuyGuard { StateName = "Cashier", TriggerEvent = "PURCHASE" });
                if (FsmHook.HasState(fsm, "State 1"))
                    guards.Add(new CatalogBuyGuard { StateName = "State 1", TriggerEvent = "DEPURCHASE" });
            }

            if (guards.Count == 0) return false;

            var results = new List<string>();
            foreach (string state in new[] { "Purchase", "Cashier", "Add", "Subtract", "State 1", "Wait" })
            {
                if (FsmHook.HasState(fsm, state)) results.Add(state);
            }

            if (results.Count == 0) return false;

            match = new CatalogBuyMatch(guards.ToArray(), results.ToArray());
            return true;
        }
    }
}
