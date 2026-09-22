// Portable engine/transport boundary doubles, NOT native Unity or game evidence.
using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;

namespace UnityEngine
{
    public struct Vector3 { }
    public struct Quaternion { public static Quaternion identity => new Quaternion(); }
    public static class Application { public static string persistentDataPath = ""; public static string loadedLevelName = "GAME"; }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static int Clamp(int n, int lo, int hi) => Math.Max(lo, Math.Min(hi, n)); }
    public sealed class GameObject
    {
        public static readonly Dictionary<string, GameObject> Objects = new Dictionary<string, GameObject>();
        public PlayMakerFSM[] Fsms = Array.Empty<PlayMakerFSM>();
        public static GameObject? Find(string path) => Objects.TryGetValue(path, out var value) ? value : null;
        public T[] GetComponents<T>() => Fsms.Cast<T>().ToArray();
    }
}
namespace HutongGames.PlayMaker
{
    public sealed class FsmInt { public int Value; }
    public sealed class FsmVariables
    {
        public readonly Dictionary<string, FsmInt> Ints = new Dictionary<string, FsmInt>();
        public FsmInt? FindFsmInt(string name) => Ints.TryGetValue(name, out var value) ? value : null;
    }
    public sealed class Fsm { public string ActiveStateName = ""; }
}
public sealed class PlayMakerFSM
{
    public string FsmName = "";
    public HutongGames.PlayMaker.FsmVariables FsmVariables = new HutongGames.PlayMaker.FsmVariables();
    public HutongGames.PlayMaker.Fsm Fsm = new HutongGames.PlayMaker.Fsm();
}
namespace WinterMP.Core
{
    internal static class WinterMPPlugin { internal static readonly ClothingTestLog Log = new ClothingTestLog(); }
    internal sealed class ClothingTestLog
    {
        public void LogWarning(string s) { }
        public void LogInfo(string s) { }
        public void LogDebug(string s) { }
    }
}
namespace WinterMP.Core.Diagnostics
{
    internal static class SyncEventLog { public static void Record(string kind, string text) { } }
}
namespace WinterMP.Core.Session
{
    public enum SessionState { Idle, Hosting, Connecting, Connected, Failed }
    internal static class GuestSaveGuard { public static bool ProtectWorld; }
    public sealed partial class SessionManager
    {
        public static SessionManager? Instance;
        public bool IsHost;
        public byte LocalPlayerId;
        public SessionState State;
        private PeerId? _hostPeer;
        private readonly Dictionary<PeerId, RemotePlayer> _playersByPeer = new Dictionary<PeerId, RemotePlayer>();
        public IEnumerable<RemotePlayer> Players => _playersByPeer.Values;
        public int PlayerCount => _playersByPeer.Count;
        public readonly List<IMessage> Sent = new List<IMessage>();
        public readonly List<IMessage> Relayed = new List<IMessage>();
        public readonly List<PeerId?> Exclusions = new List<PeerId?>();
        public void SendWorldMessage(IMessage message, Channel channel) => Sent.Add(PacketCodec.Decode(PacketCodec.Encode(message)));
        private void Broadcast(IMessage message, Channel channel, PeerId? except = null)
        { Relayed.Add(PacketCodec.Decode(PacketCodec.Encode(message))); Exclusions.Add(except); }
        public void SelectHost(PeerId peer) => _hostPeer = peer;
        public void SetAdmission(ulong value) { LocalClothingAdmission = value; _clothingSequence = 0; }
        public void Admit(RemotePlayer player) => _playersByPeer[player.Peer] = player;
        public bool Receive(PeerId peer, PlayerClothingState message) => HandlePlayerClothingState(peer, message);
        public GuestSpawn Offer(RemotePlayer guest) { var offer = new GuestSpawn(); PopulateGuestClothing(offer, guest); return offer; }
        public static ulong NewAdmission() => NewClothingAdmission();
    }
}
namespace WinterMP.Core.Sync
{
    public sealed partial class PlayerSyncManager
    {
        public static PlayerSyncManager? Instance;
        private readonly GuestResumePolicy _guestResume = new GuestResumePolicy();
        private GuestSpawn? _pendingSpawnOffer;
        public bool IsLocalSpawnReady;
        private void WatchLevelChanges() { }
        public GuestSpawn? Pending => _pendingSpawnOffer;
        // The unlinked UI/relocator completes here; clothing restore itself is linked production source.
        public void FinishSpawn() { _pendingSpawnOffer = null; _guestResume.Choose(); _guestResume.CompleteRelocation(); IsLocalSpawnReady = true; }
    }
    public sealed class WorldSyncManager
    {
        public static WorldSyncManager? Instance;
        internal readonly ClothingSync Clothing = new ClothingSync();
        public int Applied;
        public void OnRemoteClothingState(PlayerClothingState message) { Applied++; Clothing.OnRemoteClothingState(message); }
    }
}
