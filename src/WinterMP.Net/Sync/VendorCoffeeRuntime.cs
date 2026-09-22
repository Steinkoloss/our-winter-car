using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public sealed class VendorCoffeeReplica
    {
        private readonly uint _machine, _cup, _epoch, _connection;
        private readonly byte _player;
        private VendorCoffeeState? _state;
        private VendorCoffeeIntent? _pending;
        private uint _sequence;
        public VendorCoffeeReplica(uint machine, uint cup, uint epoch, byte player, uint connection)
        { _machine = machine; _cup = cup; _epoch = epoch; _player = player; _connection = connection; }
        public VendorCoffeeState? Snapshot() => _state?.Copy();
        public bool Apply(VendorCoffeeState state)
        {
            if (!VendorCoffeePolicy.Valid(state) || state.MachineId != _machine || state.CupId != _cup || state.Epoch != _epoch) return false;
            if (_state != null)
            {
                if (state.Revision <= _state.Revision || state.Generation < _state.Generation) return false;
                if (state.Generation == _state.Generation
                    && (_state.Lifecycle == VendorCoffeeLifecycle.Retired && state.Lifecycle != VendorCoffeeLifecycle.Retired
                        || state.Lifecycle < _state.Lifecycle || (state.CompletedActions & _state.CompletedActions) != _state.CompletedActions)) return false;
            }
            _state = state.Copy(); return true;
        }
        public bool Expect(VendorCoffeeIntent request)
        {
            if (_pending != null || _state == null || !VendorCoffeePolicy.Valid(request) || request.MachineId != _machine || request.CupId != _cup
                || request.Epoch != _epoch || request.Generation != _state.Generation || request.ExpectedRevision != _state.Revision
                || request.PlayerId != _player || request.Connection != _connection || request.Sequence <= _sequence) return false;
            _sequence = request.Sequence; _pending = request.Copy(); return true;
        }
        // Only live, actor-correlated receipts can authorize a personal drink effect.
        // Snapshots NEVER call this path. Reliable ordered sends state before result.
        public bool TakeDrink(VendorCoffeeResult result)
        {
            if (_pending == null || _state == null || !VendorCoffeePolicy.Valid(result) || result.MachineId != _machine || result.CupId != _cup
                || result.Epoch != _epoch || result.Connection != _connection || result.PlayerId != _player || result.Sequence != _pending.Sequence
                || result.Action != _pending.Action || result.Generation != _pending.Generation || result.Generation != _state.Generation
                || _pending.ExpectedRevision == uint.MaxValue || result.Revision != _pending.ExpectedRevision + 1 || result.Revision > _state.Revision
                || (_state.CompletedActions & VendorCoffeePolicy.Bit(result.Action)) == 0) return false;
            _pending = null; return result.Action == VendorCoffeeAction.Drink;
        }
        public void ExpirePending() { _pending = null; }
    }

    // Existing Session/World/item seams call this port. The parameterless form is
    // deliberately inert: catalog metadata alone must never install native hooks.
    public sealed class VendorCoffeeRuntime
    {
        private VendorCoffeeAuthority? _authority;
        private VendorCoffeeReplica? _replica;
        public VendorCoffeeRuntime() { }
        public VendorCoffeeRuntime(VendorCoffeeAuthority authority) { _authority = authority; }
        public VendorCoffeeRuntime(VendorCoffeeReplica replica) { _replica = replica; }
        public bool Enabled => _authority != null || _replica != null;
        public VendorCoffeeState? Snapshot() => _authority != null ? _authority.Snapshot() : _replica?.Snapshot();
        public bool OnIntent(VendorCoffeeIntent intent, VendorCoffeeActor actor, out VendorCoffeeResult? result)
        {
            result = null; return _authority != null && _authority.TryAccept(intent, actor, out result);
        }
        public bool OnState(VendorCoffeeState state) => _replica != null && _replica.Apply(state);
        public bool OnResult(VendorCoffeeResult result) => _replica != null && _replica.TakeDrink(result);
        public bool Connect(byte actor, uint connection) => _authority != null && _authority.Connect(actor, connection);
        public void Forget(byte actor) { _authority?.Forget(actor); _replica?.ExpirePending(); }
        public void Clear() { _authority?.Stop(); _replica?.ExpirePending(); _authority = null; _replica = null; }
    }
}
