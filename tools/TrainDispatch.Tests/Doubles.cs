using System;
using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Transport;
using WinterMP.Core.Session;
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace UnityEngine { internal static class Time { internal static float unscaledTime; } }
namespace WinterMP.Core
{
    internal static class WinterMPPlugin { internal static readonly LogDouble Log = new LogDouble(); }
    internal sealed class LogDouble
    {
        internal readonly List<string> Lines = new List<string>();
        internal void LogWarning(string s) => Lines.Add(s);
        internal void LogError(string s) => Lines.Add(s);
        internal void LogInfo(string s) => Lines.Add(s);
        internal void LogDebug(string s) => Lines.Add(s);
    }
}
namespace WinterMP.Core.Diagnostics
{
    internal static class SyncEventLog
    {
        internal static readonly List<string> Entries = new List<string>();
        internal static int Dumps;
        internal static void Record(string category, string detail) => Entries.Add(category + ": " + detail);
        internal static void DumpToFile() { Dumps++; }
        internal static void Clear() => Entries.Clear();
    }
}
namespace WinterMP.Core.Session
{
    public enum SessionState { Idle, Hosting, Connecting, Connected, Failed }
    internal sealed class NetTrafficMeter
    {
        internal static readonly NetTrafficMeter Instance = new NetTrafficMeter();
        internal void RecordReceived(int count) { }
    }
    internal sealed class RemotePlayer { internal byte PlayerId; }
    internal sealed class Seats { internal readonly List<IMessage> Occupants = new List<IMessage>(); }
    internal sealed partial class SessionManager
    {
        internal static SessionManager? Instance;
        internal SessionState State;
        internal bool IsHost => State == SessionState.Hosting;
        internal byte LocalPlayerId => IsHost ? (byte)0 : (byte)1;
        private PeerId? _hostPeer;
        private readonly Dictionary<PeerId, RemotePlayer> _playersByPeer = new Dictionary<PeerId, RemotePlayer>();
        private readonly Dictionary<PeerId, float> _nextResyncRequestAt = new Dictionary<PeerId, float>();
        private readonly Dictionary<PeerId, float> _nextSnapshotRequestAt = new Dictionary<PeerId, float>();
        private const float ResyncRequestCooldownSeconds = 2f, SnapshotRequestCooldownSeconds = 2f;
        private readonly Seats _passengerSeats = new Seats();
        internal readonly List<IMessage> Sent = new List<IMessage>();
        internal Action<IMessage> BeforeSend = _ => { };
        internal void Admit(PeerId peer) { _hostPeer = peer; _playersByPeer[peer] = new RemotePlayer { PlayerId = 1 }; }
        internal void Packet(PeerId peer, byte[] bytes, Channel channel) => OnPacketReceived(peer, bytes, channel);
        internal void SendTo(PeerId peer, IMessage message, Channel channel)
        { BeforeSend(message); Sent.Add(message); }
        private static GuestSpawn BuildGuestSpawn(RemotePlayer player) => new GuestSpawn();
    }
}
namespace WinterMP.Core.Sync
{
    internal static class WorldSyncIds { internal const byte NoOwner = 255; }
    internal sealed partial class TrainDouble
    {
        internal bool _failed = false;
        internal TrainState? _remote;
        internal uint _presentedHorn;
        internal float _receivedAt;
        internal int Snapshots;
        internal Func<TrainState?> OnSnapshot = () => new TrainState { Sequence = 1 };
        internal TrainState? Snapshot() { Snapshots++; return OnSnapshot(); }
    }
    public sealed partial class WorldSyncManager
    {
        internal static WorldSyncManager? Instance;
        internal uint IdHash => 0;
        private const int MaxSyncErrors = 8;
        private const float SyncErrorBackoffSeconds = 1f;
        private bool _worldSyncDisabled;
        private int _syncErrorCount;
        private float _syncErrorBackoffUntil;
        internal bool _syncReady = true, Game = true;
        internal int Prepared, Cleanup;
        internal Action OnPrepare = () => { };
        internal readonly TrainDouble _train = new TrainDouble();
        internal int Errors => _syncErrorCount;
        internal bool Disabled => _worldSyncDisabled;
        internal float Backoff => _syncErrorBackoffUntil;
        internal void Reset() { ResetTrainMessageErrors(); Cleanup++; }
        internal void GlobalError(Exception e) => HandleSyncError("Update", e);
        private bool IsGameLevel() => Game;
        private void EnsureSyncReady() { Prepared++; OnPrepare(); _syncReady = true; }
        internal static readonly Dictionary<uint, string> Labels = new Dictionary<uint, string>();
        internal static uint Label(string name)
        { uint id = StableHash.Fnv1a32(name); Labels[id] = name; return id; }
        private IEnumerable<IMessage> BuildItemDespawnSnapshots() { yield return new PingMessage { Nonce = Label("despawns") }; }
        public IMessage? BuildTimeSync() => new TimeSync();
        public IMessage? BuildWalletState() => new WalletState();
        public IEnumerable<IMessage> BuildClothingSnapshot(byte owner, byte joining) { yield return new PlayerClothingState(); }
        public void ForceHeatSourceBroadcast() { }
        public void ForceGamblingBroadcast() { }
        public void ForceUtilityBillBroadcast() { }
        public void ForceLotteryBroadcast() { }
    }
}
