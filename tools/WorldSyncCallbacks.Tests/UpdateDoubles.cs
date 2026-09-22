using System;
using WinterMP.Core.Session;

// Compile-time dependencies only. No native state, authority, or packet results
// are emulated; these callbacks expose call order and injected escaping errors.
namespace WinterMP.Net { internal enum Channel { ReliableOrdered } }
namespace WinterMP.Net.Messages { internal sealed class WorldSnapshotRequest { internal uint IdHash; } }
namespace WinterMP.Core.Session
{
    internal static class GuestSaveGuard { internal static bool ProtectWorld; }
}
namespace WinterMP.Core.Sync
{
    internal partial class UpdateDouble
    {
        internal Action<SessionManager> OnUpdate = _ => { };
        internal void Update(SessionManager session) => OnUpdate(session);
        internal void Update(SessionManager session, UpdateDouble items) => OnUpdate(session);
        internal void ProcessBags(SessionManager session) => OnUpdate(session);
        internal void ProcessPendingSpawns(SessionManager session) { }
        internal void ProcessMilk(SessionManager session) { }
        internal void UpdateItems(SessionManager session) { }
        internal void UpdateAtf(SessionManager session) { }
        internal void UpdateBanking(SessionManager session) { }
        internal void UpdateFirewoodBuyers(SessionManager session) { }
        internal void RunCargoTest(SessionManager session, float delay) { }
        internal void ProcessPending() { }
        internal void RunDoorTest() { }
        internal int DoorCount;
        internal object? BuildMessage() => null;
        internal bool ShouldBroadcast(object message, float now) => false;
    }

    internal sealed partial class VehicleDouble
    {
        internal Action<string, SessionManager> OnUpdate = (_, __) => { };
        internal Action OnProtection = () => { };
        internal void PrepareGuestDamageIsolation() => OnProtection();
        internal void PrepareGuestEngineProtection() { }
        internal void UpdateVehicleStates(SessionManager s) => OnUpdate("UpdateVehicleStates", s);
        internal void UpdateStarterDraws(SessionManager s) => OnUpdate("UpdateStarterDraws", s);
        internal void UpdateStarterWear(SessionManager s) => OnUpdate("UpdateStarterWear", s);
        internal void UpdateVehicleCoolant(SessionManager s) => OnUpdate("UpdateVehicleCoolant", s);
        internal void UpdateDrivetrainWearStates(SessionManager s) => OnUpdate("UpdateDrivetrainWearStates", s);
        internal void UpdateWheelHealthStates(SessionManager s) => OnUpdate("UpdateWheelHealthStates", s);
        internal void UpdateVehicleDamage(SessionManager s) => OnUpdate("UpdateVehicleDamage", s);
        internal void UpdateVehicleCondition(SessionManager s) => OnUpdate("UpdateVehicleCondition", s);
        internal void UpdateFuelTransfers(SessionManager s) => OnUpdate("UpdateFuelTransfers", s);
        internal void UpdateVehicleClimate(SessionManager s) => OnUpdate("UpdateVehicleClimate", s);
    }

    public sealed partial class WorldSyncManager
    {
        private const float PendingRetrySeconds = .5f, TimeSyncIntervalSeconds = 30f, ChecksumIntervalSeconds = 20f;
        private float _nextPendingAt, _nextTimeSyncAt, _nextChecksumAt;
        private bool _snapshotRequested, _selfTest = false;
        private float _cargoTestDelay = 0f;
        private uint IdHash = 0;
        private readonly UpdateDouble _items = new UpdateDouble(), _fsm = new UpdateDouble(),
            _wallet = new UpdateDouble(), _timeWeather = new UpdateDouble(), _atf = new UpdateDouble(),
            _npcTraffic = new UpdateDouble(), _train = new UpdateDouble(), _clothing = new UpdateDouble(),
            _heat = new UpdateDouble(), _fluids = new UpdateDouble(), _kilju = new UpdateDouble(),
            _progress = new UpdateDouble(), _jobSites = new UpdateDouble(), _mailOrders = new UpdateDouble(),
            _inspection = new UpdateDouble(), _police = new UpdateDouble(), _homeStereo = new UpdateDouble(),
            _rally = new UpdateDouble(), _iceRace = new UpdateDouble(), _iceRaceEvent = new UpdateDouble(),
            _iceRaceResults = new UpdateDouble(), _gambling = new UpdateDouble(), _poker = new UpdateDouble(),
            _utilityBills = new UpdateDouble(), _lottery = new UpdateDouble(), _lottoTickets = new UpdateDouble(),
            _repairShop = new UpdateDouble(), _fleaSale = new UpdateDouble(), _taxiJob = new UpdateDouble(),
            _worldScalars = new UpdateDouble(), _hockey = new UpdateDouble(), _woodDelivery = new UpdateDouble(),
            _welfare = new UpdateDouble(), _hitchhiker = new UpdateDouble(), _wanted = new UpdateDouble(),
            _jail = new UpdateDouble(), _pursuit = new UpdateDouble(), _rallyResults = new UpdateDouble(),
            _jokkis = new UpdateDouble(), _appliances = new UpdateDouble(), _phone = new UpdateDouble(),
            _pissAreas = new UpdateDouble(), _carRadio = new UpdateDouble();
        internal Action OnDiscovery = () => { };
        private void EnsureSyncReady() { _syncReady = true; }
        private void UpdateWorldDiscovery() => OnDiscovery();
        private object? BuildStateChecksum() => null;
        private void HandleDevKeys(SessionManager session) { }
        internal void BindVehicleUpdates(Action<string, SessionManager> callback) => _vehicles.OnUpdate = callback;
        internal void BindSurroundingUpdates(Action<string, SessionManager> callback)
        {
            _items.OnUpdate = s => callback("bags", s);
            _train.OnUpdate = s => callback("train", s);
            _clothing.OnUpdate = s => callback("clothing", s);
            _ventti.OnUpdate = s => callback("ventti", s);
            _trailer.OnUpdate = s => callback("trailer", s);
            _carRadio.OnUpdate = s => callback("carRadio", s);
        }
        internal void BindProtection(Action callback) => _vehicles.OnProtection = callback;
        internal void SetDoorCount(int count) => _fsm.DoorCount = count;
    }
}
