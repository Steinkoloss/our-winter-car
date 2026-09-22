using System;
using System.Linq;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WoodstoveGameplay.Tests
{
    public sealed partial class SaunaTests
    {
        // Round 000128: source-linked discovery and deliberately RED water integration
        // regressions. The archived Steam action subtracts 10 StoveHeat. No finite
        // dipper amount is invented here: its native debit/contact operands are missing.
        [Fact]
        [Trait("H08Water", "Discovery")]
        public void Nested_steam_resolves_only_at_the_audited_Kiuas_path()
        {
            var f = new Fixture(false);
            var steam = AddSteam(f, SaunaTimerAuthority.SteamPath, out var cooling);
            var root = GameObject.Find(Root)!.transform;
            var lookup = typeof(HeatSourceSync).GetMethod("FindChildFsm", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Same(steam, lookup.Invoke(null, new object[] { root, "Kiuas/StoveTrigger", "Steam" }));
            Assert.Null(lookup.Invoke(null, new object[] { root, "StoveTrigger", "Steam" }));
            Assert.NotNull(steam.FsmVariables.FindFsmBool("Water"));
            Assert.Null(steam.FsmVariables.FindFsmFloat("Water"));
            f.Sync.Update(f.Session);
            Assert.Equal(0, cooling.Calls);
            Assert.DoesNotContain(f.Session.Sent, m => m is HeatSourceIntent);
        }

        [Fact]
        [Trait("H08Water", "BlockedRegression")]
        public void Nested_steam_fixture_entry_must_not_mutate_guest_before_host_acceptance()
        {
            var guest = new Fixture(false);
            var steam = AddSteam(guest, SaunaTimerAuthority.SteamPath, out var cooling);
            guest.Sync.Update(guest.Session);
            var heat = guest.Simulation.FsmVariables.FindFsmFloat("StoveHeat");
            float before = heat.Value;
            Enter(steam, "Steam"); // Native-state boundary injection, NOT physical dipper input.
            Assert.Equal(before, heat.Value);
            Assert.Equal(0, cooling.Calls);
        }

        [Fact]
        [Trait("H08Water", "BlockedRegression")]
        public void Nested_steam_fixture_entry_must_emit_an_explicit_guest_request()
        {
            var guest = new Fixture(false);
            var steam = AddSteam(guest, SaunaTimerAuthority.SteamPath, out var cooling);
            guest.Sync.Update(guest.Session);
            guest.Session.Sent.Clear();
            Enter(steam, "Steam");
            var request = Assert.Single(guest.Session.Sent);
            // Neither legacy event-only heat intents nor timer intents carry water authority.
            Assert.IsNotType<HeatSourceIntent>(request);
            Assert.IsNotType<SaunaTimerIntent>(request);
            Assert.Equal(0, cooling.Calls);
        }

        [Fact]
        [Trait("H08Water", "BlockedRegression")]
        public void Electric_sauna_must_not_accept_a_direct_generic_trigger_without_resource_proof()
        {
            var host = new Fixture(true);
            var decoy = AddSteam(host, "StoveTrigger", out var cooling);
            host.Sync.Update(host.Session);
            host.Session.Sent.Clear();
            var request = (HeatSourceIntent)PacketCodec.Decode(PacketCodec.Encode(new HeatSourceIntent
            {
                SourceId = SaunaTimerAuthority.SourceId,
                Action = HeatSourceIntent.ActionSaunaThrow,
                PlayerId = 1,
                Sequence = 1
            }));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(request.Id, true, true, false, true));
            Assert.True(SessionMessagePolicy.IsChannelAllowed(request.Id, Channel.ReliableOrdered));
            Assert.False(host.Sync.TryAcceptIntent(request));
            Assert.Empty(decoy.Events);
            Assert.Equal(0, cooling.Calls);
            Assert.Empty(host.Session.Sent);
        }

        static PlayMakerFSM AddSteam(Fixture f, string child, out SubtractFsmFloat cooling)
        {
            var root = GameObject.Find(Root)!;
            var obj = new GameObject(Root + "/" + child);
            root.transform.Children[child] = obj.transform;
            var steam = obj.Add(new PlayMakerFSM { FsmName = "Steam", ActiveStateName = "Idle" });
            steam.FsmVariables.Vars["Water"] = new FsmBool { Name = "Water" };
            cooling = new SubtractFsmFloat
            {
                Target = f.Simulation.FsmVariables.FindFsmFloat("StoveHeat"),
                subtractValue = new FsmFloat { Value = 10 }
            };
            // Only the audited mutation boundary is doubled, not a fabricated complete
            // native graph. No transitions, water-debit action or result are synthesized.
            steam.Fsm.States = new[] { State("Idle"), State("Steam", cooling) };
            return steam;
        }
    }

    public sealed class SubtractFsmFloat : FsmStateAction
    {
        public FsmFloat Target = null!, subtractValue = null!;
        public int Calls;
        public override void OnEnter() { Calls++; Target.Value -= subtractValue.Value; }
    }
}
