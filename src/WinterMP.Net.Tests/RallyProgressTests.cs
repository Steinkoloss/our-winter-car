using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class RallyProgressTests
{
    private const ulong Token = 0x0102030405060708;
    private static RallyIntent Report(byte checkpoint = 0, ushort sequence = 1, byte player = 2, byte stage = 1, ulong token = Token)
        => new() { PlayerId = player, Stage = stage, Checkpoint = checkpoint, Sequence = sequence, ReportToken = token };
    private static T Wire<T>(T message) where T : IMessage => Assert.IsType<T>(PacketCodec.Decode(PacketCodec.Encode(message)));
    private static RallyProgressReplica StartedReplica()
    {
        var replica = new RallyProgressReplica(Token);
        replica.Observe(2, 1, false, 0, 0);
        replica.Observe(2, 1, true, 0, 1);
        return replica;
    }

    [Fact]
    public void WireAppendsConnectionTokenAndExactAcknowledgmentWithoutChangingOldFields()
    {
        var request = Report(3, 0x5566, stage: 2);
        Assert.Equal(new byte[] { 87, 0, 2, 2, 3, 0x66, 0x55, 8, 7, 6, 5, 4, 3, 2, 1 }, PacketCodec.Encode(request));
        var state = new RallyState { PlayerId = 2, Stage = 2, Phase = 2, Checkpoint = 3, Sequence = 0x1122,
            ElapsedCentiseconds = 0x33445566, Revision = 0x778899aa, ReportToken = Token, ReportSequence = 0x5566, Flags = 1 };
        Assert.Equal(new byte[] { 86, 0, 2, 2, 2, 3, 0x22, 0x11, 0x66, 0x55, 0x44, 0x33,
            0xaa, 0x99, 0x88, 0x77, 8, 7, 6, 5, 4, 3, 2, 1, 0x66, 0x55, 1 }, PacketCodec.Encode(state));
        foreach (IMessage message in new IMessage[] { request, state })
        {
            var packet = PacketCodec.Encode(message);
            for (int length = 2; length < packet.Length; length++)
                Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet.Take(length).ToArray()));
            Assert.Equal(packet, PacketCodec.Encode(PacketCodec.Decode(packet)));
        }
    }

    [Fact]
    public void RejectedStartRetriesTheSameReportAndLostAckCannotRestartTheClock()
    {
        var guest = StartedReplica(); var host = new RallyProgressLedger();
        var start = guest.Take(1)!;
        Assert.False(host.TryAccept(Wire(start), 4, 1, false, out _));
        Assert.Null(guest.Take(1.1f));
        var retry = guest.Take(1.25f)!;
        Assert.Equal(PacketCodec.Encode(start), PacketCodec.Encode(retry));
        Assert.True(host.TryAccept(Wire(retry), 4, 1.25f, true, out var ack));
        Assert.Equal(0u, ack.ElapsedCentiseconds);
        Assert.True(host.TryAccept(Wire(start), 4, 5.25f, false, out ack));
        Assert.Equal(400u, ack.ElapsedCentiseconds);
        Assert.True(guest.Receive(Wire(ack)));
        Assert.Equal(0, guest.PendingCount);
        Assert.Null(guest.Take(6));
    }

    [Fact]
    public void InvalidOrOutOfOrderReportDoesNotConsumeSequenceOrMutateProgress()
    {
        var host = new RallyProgressLedger();
        Assert.True(host.TryAccept(Report(), 4, 0, true, out _));
        Assert.False(host.TryAccept(Report(3, 99), 4, 1, true, out _));
        Assert.False(host.TryAccept(Report(1, 2), 4, 1, false, out _));
        Assert.False(host.TryAccept(Report(1, 2, stage: 2), 4, 1, true, out _));
        Assert.False(host.TryAccept(Report(1, 1), 4, 1, true, out _));
        Assert.False(host.TryAccept(Report(0, 1, stage: 2), 4, 1, true, out _));
        Assert.True(host.TryAccept(Report(1, 2), 4, 2, true, out var state));
        Assert.Equal(1, state.Checkpoint); Assert.Equal(200u, state.ElapsedCentiseconds);
        Assert.False(host.TryAccept(Report(2, 3), 3, 3, true, out _));
        Assert.True(host.TryAccept(Report(2, 3), 4, 3, true, out _));
    }

    [Fact]
    public void ValidationRejectsImpossibleIdsClocksAndSequencesBeforeCreatingRecords()
    {
        var host = new RallyProgressLedger();
        foreach (var bad in new[] { Report(player: 255), Report(stage: 0), Report(stage: 4), Report(7), Report(token: 0) })
            Assert.False(host.TryAccept(bad, 6, 0, true, out _));
        foreach (float now in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1 })
            Assert.False(host.TryAccept(Report(), 4, now, true, out _));
        Assert.False(host.TryAccept(Report(), 0, 0, true, out _));
        Assert.False(host.TryAccept(Report(), 7, 0, true, out _));
        Assert.Empty(host.Snapshots(0, false));
        Assert.True(host.TryAccept(Report(sequence: ushort.MaxValue), 4, 2, true, out _));
        Assert.True(host.TryAccept(Report(1, 0), 4, 3, true, out _));
        Assert.False(host.TryAccept(Report(2, 32768), 4, 4, true, out _));
        Assert.False(host.TryAccept(Report(2, 1), 4, 1, true, out _));
        Assert.True(host.TryAccept(Report(2, 1), 4, 4, true, out _));
    }

    [Fact]
    public void GuestCrossingsQueueInOrderEvenWhenSeveralFlagsArriveTogether()
    {
        var guest = StartedReplica(); var host = new RallyProgressLedger();
        guest.Observe(2, 1, true, 7, 1);
        Assert.Equal(4, guest.PendingCount);
        for (byte checkpoint = 0; checkpoint <= 3; checkpoint++)
        {
            var report = guest.Take(1 + checkpoint)!;
            Assert.Equal(checkpoint, report.Checkpoint);
            Assert.True(host.TryAccept(Wire(report), 4, 1 + checkpoint, true, out var state));
            Assert.True(guest.Receive(Wire(state)));
        }
        guest.Observe(2, 1, true, 7, 5);
        Assert.Equal(0, guest.PendingCount);
    }

    [Fact]
    public void InitialSaveFlagsAndReconnectDoNotInventAStartOrReplayPastCrossings()
    {
        var guest = new RallyProgressReplica(Token);
        guest.Observe(2, 1, true, 7, 0);
        Assert.Null(guest.Take(0));
        guest.Observe(2, 1, true, 15, 1);
        Assert.Equal(4, guest.Take(1)!.Checkpoint);
        guest.Clear(Token + 1);
        guest.Observe(2, 1, true, 15, 2);
        Assert.Null(guest.Take(2));
        guest.Observe(2, 1, false, 0, 3);
        guest.Observe(2, 1, true, 0, 4);
        Assert.Equal(0, guest.Take(4)!.Checkpoint);
    }

    [Fact]
    public void ForeignOrPreviousConnectionAcknowledgmentCannotSettleNewPendingStart()
    {
        var guest = StartedReplica(); var host = new RallyProgressLedger();
        var report = guest.Take(1)!;
        Assert.True(host.TryAccept(report, 4, 1, true, out var ack));
        var wrong = Wire(ack); wrong.ReportToken++;
        Assert.True(guest.Receive(wrong)); Assert.Equal(1, guest.PendingCount);
        wrong = Wire(ack); wrong.PlayerId = 3;
        Assert.True(guest.Receive(wrong)); Assert.Equal(1, guest.PendingCount);
        wrong = Wire(ack); wrong.Revision++; wrong.ReportSequence++;
        Assert.True(guest.Receive(wrong)); Assert.Equal(1, guest.PendingCount);
        Assert.True(host.TryAccept(report, 4, 2, false, out ack));
        Assert.True(host.TryAccept(report, 4, 2, false, out ack));
        Assert.True(guest.Receive(ack)); Assert.Equal(0, guest.PendingCount);
    }

    [Fact]
    public void AdmissionDropsOldReportIdentityButPreservesTheRaceAndHostClock()
    {
        var host = new RallyProgressLedger();
        host.TryAccept(Report(), 4, 10, true, out _);
        host.TryAccept(Report(1, 2), 4, 11, true, out _);
        Assert.False(host.TryAccept(Report(2, 1, token: Token + 1), 4, 12, true, out _));
        host.ForgetPlayer(2);
        var snapshot = host.Snapshots(12, false).Single();
        Assert.Equal(1, snapshot.Checkpoint); Assert.Equal(200u, snapshot.ElapsedCentiseconds);
        Assert.Equal(0, snapshot.Flags); Assert.Equal(0ul, snapshot.ReportToken);
        Assert.True(host.TryAccept(Report(2, 1, token: Token + 1), 4, 13, true, out var state));
        Assert.Equal(300u, state.ElapsedCentiseconds);
        Assert.False(host.TryAccept(Report(3, 3), 4, 14, true, out _));
    }

    [Fact]
    public void FinalTimeFreezesAndJoinSnapshotsCannotSwallowHostFinishBroadcast()
    {
        var host = new RallyProgressLedger();
        Assert.True(host.AdvanceLocal(0, 1, 0, 1, 0));
        Assert.True(host.AdvanceLocal(0, 1, 1, 1, 0));
        Assert.Equal(0u, host.Snapshots(100, false).Single().ElapsedCentiseconds);
        var final = host.Snapshots(200, true).Single();
        Assert.Equal(RallyState.PhaseFinished, final.Phase); Assert.Equal(0u, final.ElapsedCentiseconds);
        Assert.Empty(host.Snapshots(300, true));
        Assert.True(host.AdvanceLocal(0, 2, 0, 6, 400));
        Assert.True(host.Snapshots(401, false).Single().Revision > final.Revision);
    }

    [Fact]
    public void DelayedFinishAcknowledgmentReturnsFrozenTimeEvenAfterEvidenceExpires()
    {
        var host = new RallyProgressLedger();
        host.TryAccept(Report(), 1, 10, true, out _);
        var finish = Report(1, 2);
        Assert.True(host.TryAccept(finish, 1, 12, true, out _));
        Assert.True(host.TryAccept(finish, 1, 100, false, out var retry));
        Assert.Equal(200u, retry.ElapsedCentiseconds);
        Assert.Equal(RallyState.PhaseFinished, retry.Phase);
        Assert.Empty(host.Snapshots(101, true));
    }

    [Fact]
    public void EvidenceSurvivesBriefDelayButCannotBeExtendedByAnUnchangedOldPose()
    {
        var evidence = new RallyCrossingEvidence(); var host = new RallyProgressLedger();
        evidence.Observe(2, 1, 0, 10, 10);
        Assert.False(evidence.Contains(3, 1, 0, 11));
        Assert.False(evidence.Contains(2, 2, 0, 11));
        Assert.False(evidence.Contains(2, 1, 1, 11));
        Assert.True(host.TryAccept(Report(), 4, 12, evidence.Contains(2, 1, 0, 12), out _));
        evidence.Observe(2, 1, 0, 10, 12.5f);
        Assert.False(evidence.Contains(2, 1, 0, 13.01f));
        evidence.Observe(2, 1, 1, 20, 14);
        Assert.False(evidence.Contains(2, 1, 1, 20));
        evidence.Observe(2, 1, 1, 14, 14);
        evidence.ForgetPlayer(2);
        Assert.False(evidence.Contains(2, 1, 1, 14));
    }

    [Fact]
    public void InvalidAndStaleStateCannotPoisonOtherPlayersOrSequenceWrap()
    {
        var guest = new RallyProgressReplica(Token);
        var state = new RallyState { PlayerId = 2, Stage = 1, Phase = 1, Revision = uint.MaxValue };
        Assert.True(guest.Receive(state));
        state.Revision = 0; state.Checkpoint = 1;
        Assert.True(guest.Receive(state));
        var bad = Wire(state); bad.Revision = 99; bad.Flags = 2;
        Assert.False(guest.Receive(bad));
        bad = Wire(state); bad.Revision = 99; bad.Flags = 1;
        Assert.False(guest.Receive(bad));
        bad = Wire(state); bad.Revision = 99; bad.Stage = 4;
        Assert.False(guest.Receive(bad));
        Assert.False(guest.Receive(state));
        state.Revision = 0x80000000;
        Assert.False(guest.Receive(state));
        state.Revision = 1;
        Assert.True(guest.Receive(state));
        state.PlayerId = 3; state.Revision = 0;
        Assert.True(guest.Receive(state));
        state.Checkpoint = 5;
        Assert.Equal(1, guest.Latest(3)!.Checkpoint);
    }

    [Fact]
    public void ExpiredQueueStopsRetryingAndANewNativeStartCanRecover()
    {
        var guest = StartedReplica();
        var original = guest.Take(1)!; original.Checkpoint = 6;
        Assert.Equal(0, guest.Take(1.25f)!.Checkpoint);
        Assert.Null(guest.Take(11)); Assert.True(guest.Failed); Assert.Equal(0, guest.PendingCount);
        guest.Observe(2, 1, true, 1, 12);
        Assert.Null(guest.Take(12));
        guest.Observe(2, 1, false, 0, 13);
        guest.Observe(2, 1, true, 0, 14);
        Assert.False(guest.Failed); Assert.Equal(0, guest.Take(14)!.Checkpoint);
    }

    [Fact]
    public void SceneRebindInSameConnectionPreservesAdmittedTokenAndNeverReusesReportSequence()
    {
        var guest = StartedReplica(); var host = new RallyProgressLedger();
        var first = guest.Take(1)!;
        Assert.True(host.TryAccept(first, 4, 1, true, out var oldAck));
        guest.ResetView();
        guest.Observe(2, 1, false, 0, 2);
        guest.Observe(2, 1, true, 0, 3);
        var next = guest.Take(3)!;
        Assert.Equal(first.ReportToken, next.ReportToken);
        Assert.NotEqual(first.Sequence, next.Sequence);
        guest.Receive(oldAck);
        Assert.Equal(1, guest.PendingCount);
        Assert.True(host.TryAccept(next, 4, 3, true, out var newAck));
        Assert.True(guest.Receive(newAck)); Assert.Equal(0, guest.PendingCount);
    }

    [Fact]
    public void SeededRacesKeepReportsOrderedAcrossTransientRejectionsAndLostAcknowledgments()
    {
        var random = new Random(4164420);
        for (int run = 0; run < 300; run++)
        {
            var guest = StartedReplica(); var host = new RallyProgressLedger();
            byte count = (byte)(run % 2 == 0 ? 4 : 6); float now = 1;
            for (byte checkpoint = 0; checkpoint <= count; checkpoint++)
            {
                guest.Observe(2, 1, checkpoint < count, (byte)((1 << checkpoint) - 1), now);
                int rejected = random.Next(3);
                for (int i = 0; i < rejected; i++)
                {
                    Assert.False(host.TryAccept(Wire(guest.Take(now)!), count, now, false, out _)); now += .5f;
                }
                var report = Wire(guest.Take(now)!);
                Assert.Equal(checkpoint, report.Checkpoint);
                Assert.True(host.TryAccept(report, count, now, true, out var state));
                if (random.Next(2) == 0)
                {
                    now += .5f;
                    Assert.True(host.TryAccept(Wire(guest.Take(now)!), count, now, false, out state));
                }
                Assert.True(guest.Receive(Wire(state)));
                Assert.Equal(0, guest.PendingCount);
                Assert.Equal(checkpoint, guest.Latest(2)!.Checkpoint);
                now += 1;
            }
            var final = host.Snapshots(now, false).Single();
            Assert.Equal(RallyState.PhaseFinished, final.Phase);
            Assert.Equal(final.ElapsedCentiseconds, host.Snapshots(now + 100, false).Single().ElapsedCentiseconds);
        }
    }
}
