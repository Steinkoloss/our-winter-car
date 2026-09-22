using System;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Session;
using WinterMP.Core.Sync;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WoodstoveGameplay.Tests
{
    public sealed partial class SaunaTests
    {
        [Theory]
        [InlineData("Screw")][InlineData("Unscrew")]
        public void Preadmission_update_and_input_leave_loaded_timer_clock_and_mesh_untouched(string input)
        {
            var f = new Fixture(false); f.Timer.Value = 51; f.Time.Value = 295;
            f.Knob.transform.Find("mesh")!.localEulerAngles = new Vector3(0, 49, 0);
            f.Sync.Update(f.Session); Enter(f.Knob, input); Enter(f.Knob, "Wait"); Enter(f.Simulation, "Heat up");
            f.Sync.Update(f.Session);
            Assert.Equal(51, f.Timer.Value); Assert.Equal(295, f.Time.Value);
            Assert.Equal(49, f.Knob.transform.Find("mesh")!.localEulerAngles.y);
            Assert.Equal(0, f.Add.Calls); Assert.Empty(f.Session.Sent);
        }

        [Fact]
        public void Guest_scroll_reaches_host_once_and_result_reconciles_both_peers_without_native_guest_replay()
        {
            var host = new Fixture(true); host.Sync.Update(host.Session); var initial = OnlyState(host);
            var guest = new Fixture(false); guest.Sync.Update(guest.Session); Receive(guest, initial);
            Enter(guest.Knob, "Screw"); var request = Assert.Single(guest.Session.Sent.OfType<SaunaTimerIntent>());
            Assert.Equal(0, guest.Timer.Value); Assert.Equal(0, guest.Add.Calls);
            host.Activate(); host.Session.Sent.Clear(); Dispatch(host, request, 1);
            var result = OnlyState(host); Assert.Equal(SaunaTimerState.Accepted, result.Status);
            Assert.Equal(1, host.Add.Calls); Assert.Equal(10, host.Timer.Value); Assert.Equal(60, host.Time.Value);
            Assert.Equal(10, host.Knob.transform.Find("mesh")!.localEulerAngles.y);
            guest.Activate(); Receive(guest, result); Receive(guest, result);
            Assert.Equal(host.Timer.Value, guest.Timer.Value); Assert.Equal(host.Time.Value, guest.Time.Value);
            Assert.Equal(host.Knob.transform.Find("mesh")!.localEulerAngles.y, guest.Knob.transform.Find("mesh")!.localEulerAngles.y);
            Assert.Equal(0, guest.Add.Calls);
            host.Activate(); Dispatch(host, request, 1);
            Assert.Equal(1, host.Add.Calls); Assert.Single(host.Session.Sent.OfType<SaunaTimerState>());
        }

        [Theory]
        [InlineData("source")][InlineData("epoch")][InlineData("actor")][InlineData("unknown-actor")]
        [InlineData("revision")][InlineData("timer-jump")][InlineData("timer-nan")]
        [InlineData("timer-zero")][InlineData("timer-negative")][InlineData("timer-large")][InlineData("timer-infinity")]
        [InlineData("dead")][InlineData("stale")][InlineData("distant")][InlineData("unavailable")]
        [InlineData("inactive")][InlineData("no-hit")][InlineData("other-collider")]
        [InlineData("distant-eye")][InlineData("nan-eye")][InlineData("zero-direction")]
        public void Host_rejects_invalid_contact_or_identity_without_timer_mutation_or_success(string invalid)
        {
            var f = new Fixture(true); f.Sync.Update(f.Session);
            var initial = OnlyState(f); var request = (SaunaTimerIntent)Request(initial, 1, 1, 10); byte actor = 1;
            var guest = new Fixture(false); guest.Sync.Update(guest.Session); Receive(guest, initial); f.Activate();
            switch (invalid)
            {
                case "source": request.SourceId++; break;
                case "epoch": request.Epoch++; break;
                case "actor": request.Actor++; break;
                case "unknown-actor": request.Actor = actor = 2; break;
                case "revision": request.ExpectedRevision++; break;
                case "timer-jump": request.Timer = 20; break;
                case "timer-nan": request.Timer = float.NaN; break;
                case "timer-zero": request.Timer = 0; break;
                case "timer-negative": request.Timer = -1; break;
                case "timer-large": request.Timer = 121; break;
                case "timer-infinity": request.Timer = float.PositiveInfinity; break;
                case "dead": f.Session.Players[0].IsDead = true; break;
                case "stale": f.Session.Players[0].LastTransformTime = 1; break;
                case "distant": f.Session.Players[0].Position = new Vector3(4, 0, 0); break;
                case "unavailable": f.Collider.enabled = false; break;
                case "inactive": f.Simulation.enabled = false; break;
                case "no-hit": Physics.Hit = null; break;
                case "other-collider": Physics.Hit = new GameObject("other-knob").Add(new BoxCollider()); break;
                case "distant-eye": request.Eye = new NetVector3(4, 0, 0); break;
                case "nan-eye": request.Eye = new NetVector3(float.NaN, 0, 0); break;
                case "zero-direction": request.Direction = default; break;
            }
            f.Session.Sent.Clear(); Dispatch(f, request, actor); Dispatch(f, request, actor);
            Assert.Equal(0, f.Add.Calls); Assert.Equal(0, f.Timer.Value); Assert.Equal(0, f.Time.Value);
            Assert.All(f.Session.Sent.OfType<SaunaTimerState>(), s => Assert.Equal(SaunaTimerState.Rejected, s.Status));
            Assert.InRange(f.Session.Sent.OfType<SaunaTimerState>().Count(), 0, 1);
            Assert.Equal(0, f.Subtract.Calls); Assert.Equal(0, f.Knob.transform.Find("mesh")!.localEulerAngles.y);
            guest.Activate(); foreach (var state in f.Session.Sent.OfType<SaunaTimerState>()) Receive(guest, state);
            guest.Sync.Update(guest.Session);
            Assert.Equal(0, guest.Add.Calls + guest.Subtract.Calls); Assert.Equal(0, guest.Timer.Value); Assert.Equal(0, guest.Time.Value);
            Assert.Equal(0, guest.Knob.transform.Find("mesh")!.localEulerAngles.y);
        }

        [Fact]
        public void Host_local_rechecks_its_own_ray_and_does_not_accept_a_guest_fixture_collider()
        {
            var host = new Fixture(true); host.Sync.Update(host.Session); host.Session.Sent.Clear();
            var guest = new Fixture(false); // Deliberately leaves Physics.Hit pointing at the guest's collider.
            SessionManager.Instance = host.Session; Enter(host.Knob, "Screw");
            Assert.Equal(0, host.Add.Calls); Assert.Equal(SaunaTimerState.Rejected, OnlyState(host).Status);
            host.Activate(); host.Session.Sent.Clear(); Enter(host.Knob, "Screw");
            Assert.Equal(1, host.Add.Calls); Assert.Equal(SaunaTimerState.Accepted, OnlyState(host).Status);
            Assert.Equal(0, guest.Add.Calls);
        }

        [Theory]
        [InlineData("throw")][InlineData("clock")]
        public void Partial_native_failure_faults_adapter_and_never_retries_or_publishes_success(string failure)
        {
            var f = new Fixture(true); f.Sync.Update(f.Session); var initial = OnlyState(f);
            var request = (SaunaTimerIntent)Request(initial, 1, 1, 10);
            if (failure == "throw") f.Add.Throw = true;
            else ((SetFsmFloat)f.Knob.Fsm.States.Single(s => s.Name == "Wait").Actions[1]).Write = null;
            f.Session.Sent.Clear(); Dispatch(f, request, 1);
            Assert.Equal(1, f.Add.Calls); Assert.Equal(10, f.Timer.Value); Assert.Equal(0, f.Time.Value);
            Assert.Empty(f.Session.Sent); request.Sequence++; Dispatch(f, request, 1);
            Enter(f.Knob, "Screw"); f.Sync.ForceBroadcast(); f.Sync.Update(f.Session);
            Assert.Equal(1, f.Add.Calls); Assert.Empty(f.Session.Sent.OfType<SaunaTimerState>());
        }

        [Fact]
        public void Native_reentrancy_and_competing_players_cannot_execute_a_second_step()
        {
            var f = new Fixture(true); f.Session.Players.Add(new RemotePlayer { PlayerId = 2 }); f.Sync.Update(f.Session);
            var initial = f.Session.Sent.OfType<SaunaTimerState>().First();
            var request = (SaunaTimerIntent)Request(initial, 1, 1, 10);
            var reentrant = (SaunaTimerIntent)Request(initial, 1, 2, 10);
            var competing = (SaunaTimerIntent)Request(initial, 2, 1, 10);
            f.Add.During = () => { Dispatch(f, request, 1); Dispatch(f, reentrant, 1); };
            f.Session.Sent.Clear(); Dispatch(f, request, 1); Dispatch(f, reentrant, 1); Dispatch(f, competing, 2);
            Assert.Equal(1, f.Add.Calls); Assert.Equal(10, f.Timer.Value); Assert.Equal(60, f.Time.Value);
            Assert.Single(f.Session.Sent.OfType<SaunaTimerState>(), s => s.Status == SaunaTimerState.Accepted);
            Assert.Single(f.Session.Sent.OfType<SaunaTimerState>(), s => s.Status == SaunaTimerState.Rejected);
        }

        [Fact]
        public void Forget_player_preserves_sauna_highwater_and_observation_admits_rejoining_client()
        {
            var f = new Fixture(true); f.Sync.Update(f.Session); var initial = OnlyState(f);
            var request = (SaunaTimerIntent)Request(initial, 1, 1, 10);
            Dispatch(f, request, 1); f.Sync.ForgetPlayer(1); f.Session.Sent.Clear(); f.Sync.ForceBroadcast(); f.Sync.Update(f.Session);
            var observation = OnlyState(f); Assert.Equal(1u, observation.HighWater); Assert.Equal(initial.Epoch, observation.Epoch);
            var client = new WinterMP.Net.Sync.SaunaTimerClient(1); Assert.True(client.Receive(true, observation));
            var next = client.Create(true, default, new NetVector3(0, 0, 1))!; Assert.Equal(2u, next.Sequence);
            f.Session.Sent.Clear(); Dispatch(f, request, 1); Assert.Empty(f.Session.Sent); Dispatch(f, next, 1);
            Assert.Equal(2, f.Add.Calls); Assert.Equal(20, OnlyState(f).Timer);
        }

        [Theory]
        [InlineData(true)][InlineData(false)]
        public void Binding_preserves_vanilla_initialization_save_states_and_clear_restores_original_actions(bool host)
        {
            var f = new Fixture(host); f.Timer.Value = 51; f.Time.Value = 295;
            var states = f.Knob.Fsm.States.ToDictionary(s => s.Name, s => s.Actions);
            var simulation = f.Simulation.Fsm.States.Single().Actions;
            f.Knob.ActiveStateName = "Load game"; f.Sync.Update(f.Session);
            Assert.Same(states["Screw"], f.Knob.Fsm.States.Single(s => s.Name == "Screw").Actions);
            f.Knob.ActiveStateName = "Wait"; UnityEngine.Time.unscaledTime += 5; f.Sync.Update(f.Session);
            Assert.Equal(51, f.Timer.Value); Assert.Equal(295, f.Time.Value); Assert.Equal(0, f.Add.Calls);
            foreach (var name in new[] { "Save game", "Load game", "Check data" })
                Assert.Same(states[name], f.Knob.Fsm.States.Single(s => s.Name == name).Actions);
            Assert.Equal(host, simulation[0].Enabled);
            f.Sync.Clear();
            foreach (var state in f.Knob.Fsm.States) Assert.Same(states[state.Name], state.Actions);
            Assert.All(states["Wait"], a => Assert.True(a.Enabled)); Assert.All(simulation, a => Assert.True(a.Enabled));
            Assert.Equal(51, f.Timer.Value); Assert.Equal(295, f.Time.Value);
        }

        [Fact]
        public void Guest_caches_early_absolute_result_until_native_load_finishes_and_then_suppresses_writers()
        {
            var host = new Fixture(true); host.Sync.Update(host.Session); var request = Request(OnlyState(host), 1, 1, 10);
            host.Session.Sent.Clear(); Dispatch(host, request, 1); var result = OnlyState(host);
            var guest = new Fixture(false); guest.Knob.ActiveStateName = "Load game"; Receive(guest, result);
            guest.Sync.Update(guest.Session); Assert.Equal(0, guest.Timer.Value);
            guest.Knob.ActiveStateName = "Wait"; UnityEngine.Time.unscaledTime += 5; guest.Sync.Update(guest.Session);
            Enter(guest.Knob, "Wait"); Enter(guest.Simulation, "Heat up");
            Assert.Equal(result.Timer, guest.Timer.Value); Assert.Equal(result.Time, guest.Time.Value); Assert.Equal(0, guest.Add.Calls);
        }

        [Theory]
        [InlineData("Screw")][InlineData("Unscrew")]
        public void Missing_native_knob_rotation_is_partial_failure_without_success_or_retry(string input)
        {
            var host = new Fixture(true); host.Timer.Value = 51; host.Time.Value = 295;
            host.Knob.transform.Find("mesh")!.localEulerAngles = new Vector3(0, 51, 0);
            ((SetRotation)host.Knob.Fsm.States.Single(s => s.Name == input).Actions[2]).Write = null;
            host.Sync.Update(host.Session); var initial = OnlyState(host);
            var guest = new Fixture(false); guest.Sync.Update(guest.Session); Receive(guest, initial);
            Enter(guest.Knob, input); var request = Assert.Single(guest.Session.Sent.OfType<SaunaTimerIntent>());
            Assert.Equal(51, guest.Timer.Value); Assert.Equal(0, guest.Add.Calls + guest.Subtract.Calls);

            host.Activate(); host.Session.Sent.Clear(); Dispatch(host, request, 1);
            Assert.Equal(1, host.Add.Calls + host.Subtract.Calls);
            Assert.Equal(request.Timer, host.Timer.Value); Assert.Equal(request.Timer * 6, host.Time.Value);
            Assert.Equal(51, host.Knob.transform.Find("mesh")!.localEulerAngles.y);
            Assert.Empty(host.Session.Sent); // Timer/Time alone must not certify a partial native turn.
            Dispatch(host, request, 1); request.Sequence++; Dispatch(host, request, 1);
            Enter(host.Knob, input); host.Sync.ForceBroadcast(); host.Sync.Update(host.Session);
            Assert.Equal(1, host.Add.Calls + host.Subtract.Calls); Assert.Empty(host.Session.Sent.OfType<SaunaTimerState>());
            guest.Activate(); guest.Sync.Update(guest.Session);
            Assert.Equal(51, guest.Timer.Value); Assert.Equal(295, guest.Time.Value);
            Assert.Equal(51, guest.Knob.transform.Find("mesh")!.localEulerAngles.y);
            Assert.Equal(0, guest.Add.Calls + guest.Subtract.Calls);
        }

        [Theory]
        [InlineData(0f, "Unscrew", 1f)][InlineData(5f, "Unscrew", 1f)]
        [InlineData(115f, "Screw", 120f)][InlineData(120f, "Unscrew", 110f)]
        [InlineData(51f, "Screw", 61f)][InlineData(51f, "Unscrew", 41f)]
        public void Guest_native_steps_and_clamps_converge_all_result_fields_once(float loaded, string input, float expected)
        {
            var host = new Fixture(true); host.Timer.Value = loaded; host.Time.Value = loaded * 6;
            host.Knob.transform.Find("mesh")!.localEulerAngles = new Vector3(0, loaded, 0);
            host.Sync.Update(host.Session); var initial = OnlyState(host);
            var guest = new Fixture(false); guest.Sync.Update(guest.Session); Receive(guest, initial);
            Enter(guest.Knob, input); var request = Assert.Single(guest.Session.Sent.OfType<SaunaTimerIntent>());
            Assert.Equal(expected, request.Timer); Assert.Equal(loaded, guest.Timer.Value);
            Assert.Equal(0, guest.Add.Calls + guest.Subtract.Calls);
            host.Activate(); host.Session.Sent.Clear(); Dispatch(host, request, 1); Dispatch(host, request, 1);
            var result = OnlyState(host); Assert.Equal(SaunaTimerState.Accepted, result.Status);
            Assert.Equal(input == "Screw" ? 1 : 0, host.Add.Calls);
            Assert.Equal(input == "Unscrew" ? 1 : 0, host.Subtract.Calls);
            Assert.Equal(expected, host.Timer.Value); Assert.Equal(expected * 6, host.Time.Value);
            Assert.Equal(expected, host.Knob.transform.Find("mesh")!.localEulerAngles.y);
            guest.Activate(); Receive(guest, result); Receive(guest, result);
            Enter(guest.Knob, "Wait"); Enter(guest.Simulation, "Heat up"); guest.Sync.Update(guest.Session);
            Assert.Equal(host.Timer.Value, guest.Timer.Value); Assert.Equal(host.Time.Value, guest.Time.Value);
            Assert.Equal(host.Knob.transform.Find("mesh")!.localEulerAngles.y, guest.Knob.transform.Find("mesh")!.localEulerAngles.y);
            Assert.Equal(0, guest.Add.Calls + guest.Subtract.Calls);
        }

        private static SaunaTimerState OnlyState(Fixture f) => Assert.Single(f.Session.Sent.OfType<SaunaTimerState>());
        private static void Enter(PlayMakerFSM fsm, string state) => fsm.Fsm.States.Single(s => s.Name == state).Enter();
    }
}
