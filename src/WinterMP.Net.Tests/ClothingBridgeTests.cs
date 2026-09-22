using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class ClothingBridgeTests : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "clothing-fixture-" + Guid.NewGuid().ToString("N"));
        private readonly PeerId _owner = new PeerId(123), _hostPeer = new PeerId(999);
        public ClothingBridgeTests()
        {
            Directory.CreateDirectory(_root);
            Application.persistentDataPath = _root;
            Application.loadedLevelName = "GAME";
            GuestSaveGuard.ProtectWorld = false;
            Time.unscaledTime = 100;
            GameObject.Objects.Clear();
            ReloadStore();
        }
        private static void ReloadStore()
        {
            typeof(GuestProfileStore).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
            ((IDictionary)typeof(GuestProfileStore).GetField("Profiles", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Clear();
        }
        public void Dispose()
        {
            SessionManager.Instance = null; WorldSyncManager.Instance = null; PlayerSyncManager.Instance = null;
            GuestSaveGuard.ProtectWorld = false; GameObject.Objects.Clear(); ReloadStore();
            Directory.Delete(_root, true);
        }
        private static void Activate(SessionManager session, WorldSyncManager world, PlayerSyncManager? player = null)
        { SessionManager.Instance = session; WorldSyncManager.Instance = world; PlayerSyncManager.Instance = player; }
        private static FsmInt LocalInt(string path, string fsmName, string name, int value)
        {
            var fsm = new PlayMakerFSM { FsmName = fsmName }; var variable = new FsmInt { Value = value };
            fsm.FsmVariables.Ints[name] = variable;
            GameObject.Objects[path] = new GameObject { Fsms = new[] { fsm } };
            return variable;
        }
        private static PlayerClothingState Copy(PlayerClothingState message) => (PlayerClothingState)PacketCodec.Decode(PacketCodec.Encode(message));
        private RemotePlayer Owner(ulong admission, bool returning = false) => new RemotePlayer {
            PlayerId = 2, Peer = _owner, SteamId = _owner.Value, ClothingAdmission = admission, ReturningGuest = returning };
        private SessionManager Guest(ulong admission)
        {
            var guest = new SessionManager { LocalPlayerId = 2, State = SessionState.Connected };
            guest.SetAdmission(admission); guest.SelectHost(_hostPeer);
            guest.Admit(new RemotePlayer { PlayerId = 0, Peer = _hostPeer }); return guest;
        }

        [Fact]
        public void ProductionOwnerReportPersistsColdReloadRestoresOnceAndRelaysToHostPeerAndLateJoin()
        {
            var host = new SessionManager { IsHost = true, State = SessionState.Hosting };
            var hostWorld = new WorldSyncManager(); var owner = Owner(71); host.Admit(owner);
            var guest = Guest(71); var guestWorld = new WorldSyncManager();
            var guestPlayer = new PlayerSyncManager { IsLocalSpawnReady = true };
            var stage = LocalInt("PLAYER/BodyTemp", "Calculations", "ClothingStage", 4);
            var type = LocalInt("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss", "Logic", "ClothingType", 2);
            GameObject.Objects["EQUIPMENTS/winter jacket(itemx)"] = new GameObject { Fsms = new[] {
                new PlayMakerFSM { FsmName = "Use", Fsm = new Fsm { ActiveStateName = "Wear" } } } };
            Activate(guest, guestWorld, guestPlayer);
            guestWorld.Clothing.Update(guest);
            var report = Assert.IsType<PlayerClothingState>(Assert.Single(guest.Sent));
            Assert.Equal(4, report.ClothingStage); Assert.Equal(2, report.ClothingType); Assert.Equal(1, report.WinterGarment);
            Activate(host, hostWorld);
            Assert.True(host.Receive(_owner, report));
            Assert.False(host.Receive(_owner, report));
            Assert.Equal(1, hostWorld.Applied); Assert.Single(host.Relayed); Assert.Equal(_owner, host.Exclusions[0]);
            string row = File.ReadAllLines(Path.Combine(_root, "wintermp-guests.json")).Single(x => !x.StartsWith("#"));
            Assert.EndsWith(",4,2,1", row);
            ReloadStore();
            Assert.True(GuestProfileStore.TryGetClothing(123, out var saved)); Assert.Equal(1, saved.WinterGarment);

            owner = Owner(72, true); host.Admit(owner);
            var offer = (GuestSpawn)PacketCodec.Decode(PacketCodec.Encode(host.Offer(owner)));
            Assert.True(offer.HasSavedClothing); Assert.Equal(72UL, offer.ClothingAdmission);
            guest = Guest(72); guestWorld = new WorldSyncManager(); guestPlayer = new PlayerSyncManager();
            stage.Value = 1; type.Value = 0; GameObject.Objects.Remove("EQUIPMENTS/winter jacket(itemx)");
            Activate(guest, guestWorld, guestPlayer);
            var staleOffer = (GuestSpawn)PacketCodec.Decode(PacketCodec.Encode(offer)); staleOffer.ClothingAdmission = 71;
            guestPlayer.OnGuestSpawn(staleOffer); Assert.Null(guestPlayer.Pending);
            guestWorld.Clothing.Update(guest); Assert.Empty(guest.Sent);
            guestPlayer.OnGuestSpawn(offer); Assert.Same(offer, guestPlayer.Pending);
            var duplicate = (GuestSpawn)PacketCodec.Decode(PacketCodec.Encode(offer)); duplicate.ClothingStage = 9;
            guestPlayer.OnGuestSpawn(duplicate); Assert.Same(offer, guestPlayer.Pending);
            guestPlayer.FinishSpawn(); guestWorld.Clothing.Update(guest);
            var resumed = Assert.IsType<PlayerClothingState>(Assert.Single(guest.Sent));
            Assert.Equal(4, resumed.ClothingStage); Assert.Equal(2, resumed.ClothingType); Assert.Equal(1, resumed.WinterGarment);
            Assert.Equal(1, stage.Value); Assert.Equal(0, type.Value); // no local FSM write
            Time.unscaledTime += 4; guestWorld.Clothing.Update(guest); Assert.Single(guest.Sent);
            Activate(host, hostWorld);
            Assert.False(host.Receive(_owner, report)); // old admission, not merely an old sequence
            Assert.True(host.Receive(_owner, resumed)); Assert.False(host.Receive(_owner, resumed));
            Assert.Equal(2, hostWorld.Applied); Assert.Equal(2, host.Relayed.Count);
            Assert.True(hostWorld.Clothing.TryGetClothing(2, out byte hostStage, out byte hostType));
            Assert.Equal(resumed.ClothingStage, hostStage); Assert.Equal(resumed.ClothingType, hostType);

            var peer = new SessionManager { State = SessionState.Connected, LocalPlayerId = 3 };
            peer.SelectHost(_hostPeer); peer.Admit(Owner(72)); var peerWorld = new WorldSyncManager();
            var snapshot = hostWorld.Clothing.BuildSnapshot(0, 3).Single(x => x.PlayerId == 2);
            Assert.Equal(resumed.Sequence, snapshot.Sequence); Assert.Equal(1, snapshot.WinterGarment);
            Activate(peer, peerWorld);
            Assert.True(peer.Receive(_hostPeer, Copy(snapshot))); Assert.False(peer.Receive(_hostPeer, Copy(snapshot)));
            Assert.Equal(1, peerWorld.Applied);
            Assert.True(peerWorld.Clothing.TryGetClothing(2, out byte peerStage, out byte peerType));
            Assert.Equal(hostStage, peerStage); Assert.Equal(hostType, peerType);
            peerWorld.Clothing.Clear(); // scene rebind retains accepted session value
            Assert.True(peerWorld.Clothing.TryGetClothing(2, out peerStage, out peerType));
            Assert.Equal(hostStage, peerStage);
            Activate(guest, guestWorld, guestPlayer); stage.Value = 3; Time.unscaledTime += 4;
            guestWorld.Clothing.Update(guest); Assert.Equal(3, ((PlayerClothingState)guest.Sent.Last()).ClothingStage);
            Assert.Equal(row, File.ReadAllLines(Path.Combine(_root, "wintermp-guests.json")).Single(x => !x.StartsWith("#")));
        }

        [Fact]
        public void ProductionAdmissionAndInvalidReportsCannotMutateOrRelayOrSaveEitherPeer()
        {
            var host = new SessionManager { IsHost = true, State = SessionState.Hosting }; host.Admit(Owner(71));
            var world = new WorldSyncManager(); Activate(host, world);
            var good = new PlayerClothingState { PlayerId = 2, Admission = 71, Sequence = 8, ClothingStage = 4, WinterGarment = 1 };
            Assert.False(host.Receive(new PeerId(456), good));
            var bad = Copy(good); bad.PlayerId = 3; Assert.False(host.Receive(_owner, bad));
            Assert.True(host.Receive(_owner, good));
            var bytes = File.ReadAllBytes(Path.Combine(_root, "wintermp-guests.json"));
            foreach (var invalid in new[] {
                new PlayerClothingState { PlayerId=2,Admission=70,Sequence=100,ClothingStage=9 },
                new PlayerClothingState { PlayerId=2,Admission=71,Sequence=100,WinterGarment=255 },
                new PlayerClothingState { PlayerId=2,Admission=71,Sequence=7,ClothingStage=9 }, Copy(good) })
                Assert.False(host.Receive(_owner, invalid));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, "wintermp-guests.json")));
            Assert.Equal(1, world.Applied); Assert.Single(host.Relayed);
            Assert.Equal(4, host.Players.Single().ClothingState!.ClothingStage);
            var peer = new SessionManager { State = SessionState.Connected, LocalPlayerId = 3 };
            peer.SelectHost(_hostPeer); peer.Admit(Owner(71)); var peerWorld = new WorldSyncManager(); Activate(peer, peerWorld);
            Assert.False(peer.Receive(_owner, good)); Assert.Equal(0, peerWorld.Applied);
            Assert.True(peer.Receive(_hostPeer, good));
            bad = Copy(good); bad.Sequence = 9; bad.WinterGarment = 3;
            Assert.False(peer.Receive(_hostPeer, bad)); Assert.Equal(1, peerWorld.Applied); Assert.Empty(peer.Relayed);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, "wintermp-guests.json")));
        }

        [Theory]
        [InlineData(true, SessionState.Hosting, 2, 71UL, "GAME")]
        [InlineData(false, SessionState.Connecting, 2, 71UL, "GAME")]
        [InlineData(false, SessionState.Connected, 3, 71UL, "GAME")]
        [InlineData(false, SessionState.Connected, 2, 70UL, "GAME")]
        [InlineData(false, SessionState.Connected, 2, 71UL, "MainMenu")]
        public void ProductionRestoreRejectsHostUnadmittedWrongIdentityStaleAndMenu(bool isHost, SessionState state, byte player, ulong admission, string level)
        {
            var session = Guest(71); session.IsHost = isHost; session.State = state;
            var local = new PlayerSyncManager(); Activate(session, new WorldSyncManager(), local); Application.loadedLevelName = level;
            local.OnGuestSpawn(new GuestSpawn { ClothingPlayerId = player, ClothingAdmission = admission,
                HasSavedClothing = true, ClothingStage = 4, ClothingType = 2, WinterGarment = 1 });
            Assert.Null(local.Pending);
            Assert.Equal(1, local.ResolveLocalClothing(new GuestProfile.ClothingSnapshot { Valid = true, ClothingStage = 1 }).ClothingStage);
        }

        [Fact]
        public void SameWorldReconnectReportsUnchangedClothingUnderNewAdmission()
        {
            var session = Guest(71); var world = new WorldSyncManager();
            var player = new PlayerSyncManager { IsLocalSpawnReady = true }; Activate(session, world, player);
            LocalInt("PLAYER/BodyTemp", "Calculations", "ClothingStage", 4);
            LocalInt("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss", "Logic", "ClothingType", 2);
            world.Clothing.Update(session); Assert.Single(session.Sent);
            session.SetAdmission(72); Time.unscaledTime += 4;
            world.Clothing.Update(session); Assert.Equal(2, session.Sent.Count);
            Assert.Equal(72UL, ((PlayerClothingState)session.Sent.Last()).Admission);
        }

        [Theory]
        [InlineData(-1, 2)] [InlineData(256, 2)] [InlineData(4, -1)] [InlineData(4, 256)]
        public void InvalidNativeTupleIsNotClampedIntoAPersistentAssertion(int stage, int type)
        {
            var session = Guest(71); var world = new WorldSyncManager();
            Activate(session, world, new PlayerSyncManager { IsLocalSpawnReady = true });
            LocalInt("PLAYER/BodyTemp", "Calculations", "ClothingStage", stage);
            LocalInt("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss", "Logic", "ClothingType", type);
            world.Clothing.Update(session); Assert.Empty(session.Sent);
        }

        [Fact]
        public void StoreIsHostOnlyAndRejectsInvalidDuplicatesAbsentProfiles()
        {
            var host = new SessionManager { IsHost = true, State = SessionState.Hosting }; Activate(host, new WorldSyncManager());
            var saved = new GuestProfile.ClothingSnapshot { Valid = true, ClothingStage = 4, ClothingType = 2, WinterGarment = 2 };
            Assert.False(GuestProfileStore.TryGetClothing(123, out _));
            Assert.True(GuestProfileStore.RememberClothing(123, saved));
            Assert.False(GuestProfileStore.RememberClothing(123, saved));
            var invalid = saved; invalid.ClothingStage = 256; Assert.False(GuestProfileStore.RememberClothing(123, invalid));
            invalid = saved; invalid.ClothingType = -1; Assert.False(GuestProfileStore.RememberClothing(123, invalid));
            invalid = saved; invalid.WinterGarment = 3; Assert.False(GuestProfileStore.RememberClothing(123, invalid));
            saved.ClothingStage = 1; host.IsHost = false; Assert.False(GuestProfileStore.RememberClothing(123, saved));
            host.IsHost = true; GuestSaveGuard.ProtectWorld = true; Assert.False(GuestProfileStore.RememberClothing(123, saved));
            Assert.True(GuestProfileStore.TryGetClothing(123, out var unchanged)); Assert.Equal(4, unchanged.ClothingStage);
            var newGuest = Owner(71); Assert.False(host.Offer(newGuest).HasSavedClothing);
            Assert.NotEqual(0UL, SessionManager.NewAdmission());
        }
    }
}
