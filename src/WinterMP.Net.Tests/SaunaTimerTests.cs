using System;
using System.Collections.Generic;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public sealed class SaunaTimerTests
    {
        [Fact]
        public void Wire_round_trip_preserves_every_field_and_exact_layout()
        {
            var intent = new SaunaTimerIntent { SourceId = SaunaTimerAuthority.SourceId, Epoch = 19, Actor = 2,
                Sequence = 47, ExpectedRevision = 31, Timer = 61, Eye = new NetVector3(1, 2, 3), Direction = new NetVector3(0, 0, 1) };
            var w = new NetWriter(); w.WriteUInt16(270); w.WriteUInt32(intent.SourceId); w.WriteUInt32(19); w.WriteByte(2);
            w.WriteUInt32(47); w.WriteUInt32(31); w.WriteSingle(61); w.WriteVector3(intent.Eye); w.WriteVector3(intent.Direction);
            Assert.Equal(w.ToArray(), PacketCodec.Encode(intent));
            Assert.Equal(w.ToArray(), PacketCodec.Encode(PacketCodec.Decode(w.ToArray())));
            var state = ValidState();
            w = RawState(state);
            Assert.Equal(w.ToArray(), PacketCodec.Encode(state));
            Assert.Equal(w.ToArray(), PacketCodec.Encode(PacketCodec.Decode(w.ToArray())));
            Assert.Equal(36, w.ToArray().Length);
        }

        [Theory]
        [InlineData("source")][InlineData("epoch")][InlineData("revision")][InlineData("status")]
        [InlineData("actor")][InlineData("sequence")][InlineData("highwater")]
        [InlineData("timer-negative")][InlineData("timer-large")][InlineData("timer-nan")]
        [InlineData("time-negative")][InlineData("time-large")][InlineData("time-infinity")]
        [InlineData("angle-negative")][InlineData("angle-large")][InlineData("angle-nan")]
        public void Invalid_states_are_rejected_on_read_write_and_client_without_mutating_replica(string invalid)
        {
            var state = ValidState(); var client = new SaunaTimerClient(2);
            Assert.True(client.Receive(true, state)); var current = client.Current;
            switch (invalid)
            {
                case "source": state.SourceId++; break;
                case "epoch": state.Epoch = 0; break;
                case "revision": state.Revision = 0; break;
                case "status": state.Status = 3; break;
                case "actor": state.Actor = 255; break;
                case "sequence": state.Sequence = 0; break;
                case "highwater": state.HighWater = state.Sequence - 1; break;
                case "timer-negative": state.Timer = -1; break;
                case "timer-large": state.Timer = 121; break;
                case "timer-nan": state.Timer = float.NaN; break;
                case "time-negative": state.Time = -1; break;
                case "time-large": state.Time = 721; break;
                case "time-infinity": state.Time = float.PositiveInfinity; break;
                case "angle-negative": state.KnobAngle = -1; break;
                case "angle-large": state.KnobAngle = 361; break;
                case "angle-nan": state.KnobAngle = float.NaN; break;
            }
            Assert.False(state.Valid);
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(state));
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(RawState(state).ToArray()));
            Assert.False(client.Receive(true, state)); Assert.Same(current, client.Current);
        }

        [Fact]
        public void Empty_state_is_not_an_admission_and_cold_zero_is_a_valid_observation()
        {
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(new SaunaTimerState()));
            var state = new SaunaTimerState { SourceId = SaunaTimerAuthority.SourceId, Epoch = 17, Revision = 1 };
            var client = new SaunaTimerClient(1);
            Assert.Null(client.Create(true, default, default));
            Assert.True(client.Receive(true, (SaunaTimerState)PacketCodec.Decode(PacketCodec.Encode(state))));
            Assert.Equal(10, client.Create(true, default, default)!.Timer);
        }

        [Fact]
        public void Guest_and_host_share_exactly_once_authority_absolute_replication_and_rejoin_highwater()
        {
            var f = new Fixture(); var guest = new SaunaTimerClient(1); var host = new SaunaTimerClient(0);
            f.Authority.Publish(1, s => { Assert.True(guest.Receive(true, s)); Assert.True(host.Receive(true, s)); });
            var request = guest.Create(true, default, default)!;
            Assert.True(f.Authority.Execute(1, request, s => { f.Results.Add(s); guest.Receive(true, s); host.Receive(true, s); }));
            Assert.Equal(1, f.Native.Turns); Assert.Equal(SaunaTimerState.Accepted, Assert.Single(f.Results).Status);
            Assert.Equal(10, guest.Current!.Timer); Assert.Equal(60, guest.Current.Time);
            Assert.Equal(PacketCodec.Encode(host.Current!), PacketCodec.Encode(guest.Current));
            Assert.False(f.Authority.Execute(1, request, f.Results.Add)); Assert.Single(f.Results);
            Assert.True(f.Authority.Execute(0, host.Create(true, default, default)!, f.Results.Add));
            Assert.Equal(2, f.Native.Turns); Assert.Equal(20, f.Native.Values.Timer);
            var rejoined = new SaunaTimerClient(1); f.Authority.Publish(1, s => rejoined.Receive(true, s));
            Assert.Equal(2u, rejoined.Create(false, default, default)!.Sequence);
            Assert.False(f.Authority.Execute(1, request, f.Results.Add)); Assert.Equal(2, f.Native.Turns);
        }

        [Theory]
        [InlineData("source")][InlineData("epoch")][InlineData("actor")][InlineData("unauthenticated")][InlineData("zero-sequence")]
        [InlineData("revision")][InlineData("timer-nan")][InlineData("timer-infinity")][InlineData("timer-negative")]
        [InlineData("timer-large")][InlineData("timer-zero")][InlineData("timer-jump")]
        [InlineData("missing")][InlineData("dead")][InlineData("unavailable")][InlineData("no-contact")]
        [InlineData("stale-pose")][InlineData("future-pose")][InlineData("nan-pose")]
        [InlineData("distant")][InlineData("negative-distance")][InlineData("infinite-distance")]
        public void Invalid_requests_never_mutate_and_replays_cannot_retry(string invalid)
        {
            var f = new Fixture(); var request = f.Request(); byte actor = 1;
            switch (invalid)
            {
                case "source": request.SourceId++; break;
                case "epoch": request.Epoch++; break;
                case "actor": request.Actor++; break;
                case "unauthenticated": actor = 255; break;
                case "zero-sequence": request.Sequence = 0; break;
                case "revision": request.ExpectedRevision++; break;
                case "timer-nan": request.Timer = float.NaN; break;
                case "timer-infinity": request.Timer = float.PositiveInfinity; break;
                case "timer-negative": request.Timer = -1; break;
                case "timer-large": request.Timer = 121; break;
                case "timer-zero": request.Timer = 0; break;
                case "timer-jump": request.Timer = 20; break;
                case "missing": f.Native.Contact.Present = false; break;
                case "dead": f.Native.Contact.Alive = false; break;
                case "unavailable": f.Native.Contact.Available = false; break;
                case "no-contact": f.Native.Contact.Contact = false; break;
                case "stale-pose": f.Native.Contact.PoseAge = 2.01f; break;
                case "future-pose": f.Native.Contact.PoseAge = -1; break;
                case "nan-pose": f.Native.Contact.PoseAge = float.NaN; break;
                case "distant": f.Native.Contact.DistanceSquared = 9.01f; break;
                case "negative-distance": f.Native.Contact.DistanceSquared = -1; break;
                case "infinite-distance": f.Native.Contact.DistanceSquared = float.PositiveInfinity; break;
            }
            Assert.False(f.Authority.Execute(actor, request, f.Results.Add));
            Assert.False(f.Authority.Execute(actor, request, f.Results.Add));
            Assert.Equal(0, f.Native.Turns); Assert.Equal(0, f.Native.Values.Timer);
            Assert.All(f.Results, s => { Assert.Equal(SaunaTimerState.Rejected, s.Status); Assert.Equal(0, s.Timer); });
            Assert.InRange(f.Results.Count, 0, 1); Assert.False(f.Authority.Faulted);
        }

        [Fact]
        public void Competing_and_stale_requests_do_not_execute_after_first_result()
        {
            var f = new Fixture(); var first = f.Request(); var competing = f.Request(); competing.Actor = 2;
            Assert.True(f.Authority.Execute(1, first, f.Results.Add));
            Assert.False(f.Authority.Execute(2, competing, f.Results.Add));
            first.Sequence = 2;
            Assert.False(f.Authority.Execute(1, first, f.Results.Add));
            Assert.Equal(1, f.Native.Turns); Assert.Equal(3, f.Results.Count);
            Assert.Equal(SaunaTimerState.Accepted, f.Results[0].Status);
            Assert.Equal(SaunaTimerState.Rejected, f.Results[1].Status);
            Assert.Equal(SaunaTimerState.Rejected, f.Results[2].Status);
        }

        [Theory]
        [InlineData("read")][InlineData("observe")][InlineData("turn")][InlineData("publish")]
        public void Reentrant_requests_are_reserved_before_callbacks_without_recursive_mutation_or_result(string phase)
        {
            var f = new Fixture(); var first = f.Request(); var nested = f.Request(); nested.Sequence = 2;
            Action callback = () =>
            {
                Assert.False(f.Authority.Execute(1, first, _ => throw new Exception("duplicate result")));
                Assert.False(f.Authority.Execute(1, nested, _ => throw new Exception("reentrant result")));
                f.Authority.Publish(1, _ => throw new Exception("reentrant observation"));
            };
            if (phase == "read") f.Native.Reading = callback;
            if (phase == "observe") f.Native.Observing = callback;
            if (phase == "turn") f.Native.Turning = callback;
            Assert.True(f.Authority.Execute(1, first, s => { f.Results.Add(s); if (phase == "publish") callback(); }));
            Assert.Equal(2u, f.Authority.Seen(1)); Assert.Equal(1, f.Native.Turns); Assert.Single(f.Results);
            Assert.False(f.Authority.Execute(1, nested, f.Results.Add)); Assert.Single(f.Results);
        }

        [Theory]
        [InlineData("read")][InlineData("observe")][InlineData("partial-throw")][InlineData("partial-clock")][InlineData("partial-knob")][InlineData("publish")]
        public void Native_or_publication_failure_latches_without_second_mutation_or_success(string phase)
        {
            var f = new Fixture(); var request = f.Request();
            Action fail = () => throw new InvalidOperationException("counted boundary failure");
            if (phase == "read") f.Native.Reading = fail;
            if (phase == "observe") f.Native.Observing = fail;
            if (phase == "partial-throw") f.Native.Turning = fail;
            if (phase == "partial-clock") f.Native.SkipClock = true;
            if (phase == "partial-knob") f.Native.SkipKnob = true;
            Assert.False(f.Authority.Execute(1, request, s => { if (phase == "publish") fail(); f.Results.Add(s); }));
            Assert.True(f.Authority.Faulted); Assert.Empty(f.Results);
            var turns = f.Native.Turns; request.Sequence++;
            Assert.False(f.Authority.Execute(1, request, f.Results.Add)); f.Authority.Publish(1, f.Results.Add);
            Assert.Equal(turns, f.Native.Turns); Assert.Empty(f.Results);
        }

        [Theory]
        [InlineData(0f, false, 1f)][InlineData(0f, true, 10f)]
        [InlineData(5f, false, 1f)][InlineData(115f, true, 120f)]
        public void Clamped_request_executes_the_matching_native_direction(float initial, bool increase, float expected)
        {
            var f = new Fixture(); f.Native.Values = new SaunaTimerValues { Timer = initial, Time = initial * 6, KnobAngle = initial };
            var client = new SaunaTimerClient(1); f.Authority.Publish(1, s => client.Receive(true, s));
            var request = client.Create(increase, default, default)!;
            Assert.Equal(expected, request.Timer);
            Assert.True(f.Authority.Execute(1, request, f.Results.Add));
            Assert.Equal(increase, f.Native.LastIncrease); Assert.Equal(1, f.Native.Turns);
            var state = Assert.Single(f.Results); Assert.Equal(SaunaTimerState.Accepted, state.Status);
            Assert.Equal(expected, state.Timer); Assert.Equal(expected * 6, state.Time); Assert.Equal(expected, state.KnobAngle);
            Assert.False(f.Authority.Execute(1, request, f.Results.Add)); Assert.Equal(1, f.Native.Turns);
        }

        [Theory]
        [InlineData(0.0001f, true)][InlineData(-0.0001f, true)]
        [InlineData(0.02f, false)][InlineData(-0.02f, false)]
        public void Native_rotation_readback_allows_only_small_euler_roundoff(float offset, bool accepted)
        {
            var f = new Fixture(); f.Native.KnobOffset = offset; var request = f.Request();
            Assert.Equal(accepted, f.Authority.Execute(1, request, f.Results.Add));
            Assert.Equal(!accepted, f.Authority.Faulted); Assert.Equal(1, f.Native.Turns);
            if (accepted) Assert.Equal(10 + offset, Assert.Single(f.Results).KnobAngle);
            else Assert.Empty(f.Results);
        }

        [Fact]
        public void Client_rejects_wrong_host_epoch_and_older_results_without_delta_replay()
        {
            var client = new SaunaTimerClient(2); var state = ValidState();
            Assert.False(client.Receive(false, state)); Assert.Null(client.Current);
            Assert.True(client.Receive(true, state)); var current = client.Current;
            state.Epoch++; Assert.False(client.Receive(true, state)); state.Epoch--;
            state.Revision--; Assert.False(client.Receive(true, state)); Assert.Same(current, client.Current);
            state.Revision++; Assert.True(client.Receive(true, state)); Assert.Equal(51, client.Current!.Timer);
            Assert.Equal(state.HighWater + 1, client.Create(true, default, default)!.Sequence);
            state.HighWater = uint.MaxValue; Assert.True(client.Receive(true, state)); Assert.Null(client.Create(true, default, default));
        }

        [Fact]
        public void Admission_requires_authenticated_guest_or_selected_admitted_host_and_ordered_channel()
        {
            foreach (var receiverHost in new[] { false, true })
            foreach (var authenticated in new[] { false, true })
            foreach (var selected in new[] { false, true })
            foreach (var admitted in new[] { false, true })
            {
                Assert.Equal(receiverHost && authenticated, SessionMessagePolicy.IsSenderAllowed(MessageId.SaunaTimerIntent, receiverHost, authenticated, selected, admitted));
                Assert.Equal(!receiverHost && selected && admitted, SessionMessagePolicy.IsSenderAllowed(MessageId.SaunaTimerState, receiverHost, authenticated, selected, admitted));
            }
            foreach (var id in new[] { MessageId.SaunaTimerIntent, MessageId.SaunaTimerState })
            {
                Assert.True(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableOrdered));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.UnreliableSequenced));
                Assert.False(SessionMessagePolicy.IsChannelAllowed(id, Channel.ReliableBulk));
            }
        }

        private static SaunaTimerState ValidState() => new SaunaTimerState { SourceId = SaunaTimerAuthority.SourceId,
            Epoch = 17, Revision = 23, Actor = 2, Sequence = 29, HighWater = 31, Status = SaunaTimerState.Accepted,
            Timer = 51, Time = 306, KnobAngle = 51 };
        private static NetWriter RawState(SaunaTimerState s)
        {
            var w = new NetWriter(); w.WriteUInt16(271); w.WriteUInt32(s.SourceId); w.WriteUInt32(s.Epoch); w.WriteUInt32(s.Revision);
            w.WriteByte(s.Actor); w.WriteUInt32(s.Sequence); w.WriteUInt32(s.HighWater); w.WriteByte(s.Status);
            w.WriteSingle(s.Timer); w.WriteSingle(s.Time); w.WriteSingle(s.KnobAngle); return w;
        }
        private sealed class Fixture
        {
            internal readonly Native Native = new Native();
            internal readonly SaunaTimerAuthority Authority;
            internal readonly List<SaunaTimerState> Results = new List<SaunaTimerState>();
            internal Fixture() { Authority = new SaunaTimerAuthority(17, Native); }
            internal SaunaTimerIntent Request()
            {
                var client = new SaunaTimerClient(1); Authority.Publish(1, s => client.Receive(true, s));
                return client.Create(true, default, default)!;
            }
        }
        private sealed class Native : ISaunaTimerNative
        {
            internal SaunaTimerValues Values;
            internal SaunaTimerContact Contact = new SaunaTimerContact { Present = true, Alive = true, Available = true, Contact = true };
            internal int Turns; internal bool SkipClock, SkipKnob;
            internal bool LastIncrease;
            internal float KnobOffset;
            internal Action? Reading, Observing, Turning;
            public SaunaTimerValues Read() { Reading?.Invoke(); return Values; }
            public SaunaTimerContact Observe(byte actor, NetVector3 eye, NetVector3 direction) { Observing?.Invoke(); return Contact; }
            public void Turn(bool increase)
            {
                Turns++; LastIncrease = increase; Values.Timer = SaunaTimerAuthority.Step(Values.Timer, increase); Turning?.Invoke();
                if (!SkipClock) Values.Time = Values.Timer * 6;
                if (!SkipKnob) Values.KnobAngle = Values.Timer + KnobOffset;
            }
        }
    }
}
