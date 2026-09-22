using System.Collections.Generic;
using WinterMP.Core.Session;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace WinterMP.Core.Sync
{
    internal sealed partial class ItemWorldSync
    {
        // Intentionally NO native adapter installation. Catalog schema 1 records
        // missing evidence, not authority to replay Purchase or a drink effect.
        // No hooks, suppressed actions, cloned objects or save writers to restore.
        private readonly VendorCoffeeRuntime _vendorCoffee = new VendorCoffeeRuntime();

        internal void OnVendorCoffeeIntent(VendorCoffeeIntent intent, byte authenticatedActor)
        {
            var session = SessionManager.Instance;
            if (session?.IsHost != true || !_vendorCoffee.Enabled) return;
            // Connection nonce, living/fresh pose and audited native geometry must
            // be supplied by the future adapter. Never trust intent.Connection.
            var unavailable = new VendorCoffeeActor(authenticatedActor, 0, true, false, false, false);
            if (!_vendorCoffee.OnIntent(intent, unavailable, out var result) || result == null) return;
            var state = _vendorCoffee.Snapshot();
            if (state != null) session.SendWorldMessage(state, Channel.ReliableOrdered);
            session.SendWorldMessage(result, Channel.ReliableOrdered);
        }
        internal void OnVendorCoffeeState(VendorCoffeeState state)
        {
            if (SessionManager.Instance?.IsHost == false) _vendorCoffee.OnState(state);
        }
        internal void OnVendorCoffeeResult(VendorCoffeeResult result)
        {
            if (SessionManager.Instance?.IsHost == false) _vendorCoffee.OnResult(result);
            // No native personal effect is selected or executed by this foundation.
        }
        internal IEnumerable<VendorCoffeeState> BuildVendorCoffeeStates()
        {
            var state = _vendorCoffee.Snapshot(); if (state != null) yield return state;
        }
        internal VendorCoffeeState? BuildVendorCoffeeState(uint id)
        {
            var state = _vendorCoffee.Snapshot();
            return state != null && (id == state.MachineId || id == state.CupId) ? state : null;
        }
        internal void ForgetVendorCoffeePlayer(byte actor) { _vendorCoffee.Forget(actor); }
        private void ClearVendorCoffee() { _vendorCoffee.Clear(); }
    }
}
