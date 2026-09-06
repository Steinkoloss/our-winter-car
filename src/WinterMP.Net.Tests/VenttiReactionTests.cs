using System;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class VenttiReactionTests
{
    private const uint Table = 0x01020304, Layout = 2;
    private static VenttiSceneState Scene(uint seq = 3, int count = 3) => new()
    {
        TableId = Table, LayoutId = Layout, Sequence = seq,
        Poses = Enumerable.Range(0, count).Select(_ => new VenttiPose { Rotation = NetQuaternion.Identity, Active = 1 }).ToArray(),
    };
    private static VenttiSoundCue Cue(uint seq = 1, float delay = 0) => new()
    {
        TableId = Table, LayoutId = Layout, Sequence = seq, Sound = 7,
        Position = new NetVector3(1.5f, -2, 3), Delay = delay,
    };

    [Fact]
    public void SceneWireOrderHasCompactSignedRotationsAndKnownWidth()
    {
        byte[] header = { 178, 0, 4, 3, 2, 1, 2, 0, 0, 0, 3, 0, 0, 0, 3 };
        var pose = new byte[21]; pose[18] = 255; pose[19] = 127; pose[20] = 1;
        Assert.Equal(header.Concat(pose).Concat(pose).Concat(pose), PacketCodec.Encode(Scene()));
        var original = Scene(); original.Poses[0].Rotation = new NetQuaternion(0, 0, 0, -1);
        original.Poses[0].Position = new NetVector3(-1200.25f, 18, 123.5f);
        var copy = Assert.IsType<VenttiSceneState>(PacketCodec.Decode(PacketCodec.Encode(original)));
        Assert.Equal(-1, copy.Poses[0].Rotation.W);
        Assert.Equal(-1200.25f, copy.Poses[0].Position.X);
        Assert.True(VenttiSceneReplica.Same(original, copy));
    }

    [Fact]
    public void EveryPermittedSceneFitsAClassicSteamUnreliablePacket()
    {
        for (int count = 3; count <= VenttiSceneState.MaxPoses; count++)
        {
            var packet = PacketCodec.Encode(Scene(count: count));
            Assert.Equal(15 + 21 * count, packet.Length);
            Assert.True(packet.Length <= 1200);
            Assert.Equal(count, Assert.IsType<VenttiSceneState>(PacketCodec.Decode(packet)).Poses.Length);
        }
        Assert.Equal(981, PacketCodec.Encode(Scene(count: 46)).Length);
        Assert.Throws<ProtocolException>(() => PacketCodec.Encode(Scene(count: VenttiSceneState.MaxPoses + 1)));
    }

    [Fact]
    public void QuaternionQuantizationKeepsRotationAndDoesNotDriftAfterReencoding()
    {
        var random = new Random(31337);
        for (int i = 0; i < 2000; i++)
        {
            double x = random.NextDouble() * 2 - 1, y = random.NextDouble() * 2 - 1;
            double z = random.NextDouble() * 2 - 1, w = random.NextDouble() * 2 - 1;
            double norm = Math.Sqrt(x*x + y*y + z*z + w*w);
            var state = Scene(); state.Poses[0].Rotation = new NetQuaternion((float)(x/norm), (float)(y/norm), (float)(z/norm), (float)(w/norm));
            var packet = PacketCodec.Encode(state);
            var copy = Assert.IsType<VenttiSceneState>(PacketCodec.Decode(packet));
            Assert.True(VenttiSceneReplica.IsValid(copy));
            Assert.InRange(Math.Abs(state.Poses[0].Rotation.X - copy.Poses[0].Rotation.X), 0, 1f / 32767);
            Assert.InRange(Math.Abs(state.Poses[0].Rotation.W - copy.Poses[0].Rotation.W), 0, 1f / 32767);
            Assert.Equal(packet, PacketCodec.Encode(copy));
        }
    }

    [Fact]
    public void TruncatedOversizedAndNoncanonicalScenePacketsFailBeforeApplication()
    {
        var packet = PacketCodec.Encode(Scene());
        for (int length = 2; length < packet.Length; length++)
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet.Take(length).ToArray()));
        var bad = (byte[])packet.Clone(); bad[14] = 57;
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bad));
        bad = (byte[])packet.Clone(); bad[14] = 2;
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bad));
        bad = (byte[])packet.Clone(); bad[35] = 2; // active flag
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bad));
        bad = (byte[])packet.Clone(); bad[27] = 0; bad[28] = 128; // reserved -32768 quaternion component
        Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bad));
    }

    [Fact]
    public void InvalidLayoutPoseAndSequenceCannotReplaceAPendingScene()
    {
        var replica = new VenttiSceneReplica();
        Assert.True(replica.Receive(Table, Layout, 3, Scene(1), 0));
        var wrong = Scene(99); wrong.TableId++;
        Assert.False(replica.Receive(Table, Layout, 3, wrong, 1));
        wrong.TableId = Table; wrong.LayoutId++;
        Assert.False(replica.Receive(Table, Layout, 3, wrong, 1));
        wrong = Scene(99, 4);
        Assert.False(replica.Receive(Table, Layout, 3, wrong, 1));
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f })
        {
            wrong = Scene(99); wrong.Poses[0].Position.X = value;
            Assert.False(replica.Receive(Table, Layout, 3, wrong, 1));
        }
        wrong = Scene(99); wrong.Poses[0].Rotation = new NetQuaternion();
        Assert.False(replica.Receive(Table, Layout, 3, wrong, 1));
        Assert.False(replica.Receive(Table, Layout, 3, Scene(99), float.NaN));
        Assert.Equal(1u, replica.Current!.Sequence);
        Assert.True(replica.Receive(Table, Layout, 3, Scene(2), 1));
        Assert.False(replica.Receive(Table, Layout, 3, Scene(1), 2));
        Assert.False(replica.Receive(Table, Layout, 3, Scene(2 + 0x80000000u), 2));
    }

    [Fact]
    public void LocalBoneBoundsCannotSmuggleWorldSizedOffsets()
    {
        var state = Scene(count: 4);
        state.Poses[3].Position.X = 1001;
        Assert.False(VenttiSceneReplica.IsValid(state));
        state.Poses[3].Position.X = 999;
        Assert.True(VenttiSceneReplica.IsValid(state));
    }

    [Fact]
    public void LateJoinCopiesTheCurrentSceneAndInterpolatesOnlyBetweenNewerSnapshots()
    {
        var replica = new VenttiSceneReplica();
        var state = Scene(uint.MaxValue);
        state.Poses[0].Position.X = 42;
        Assert.True(replica.Receive(Table, Layout, 3, state, 10));
        state.Poses[0].Position.X = 999;
        Assert.Equal(42, replica.Current!.Poses[0].Position.X);
        Assert.Equal(1, replica.Blend(10));
        var next = Scene(0); next.Poses[0].Position.X = 43;
        Assert.True(replica.Receive(Table, Layout, 3, next, 10));
        Assert.Equal(42, replica.Previous!.Poses[0].Position.X);
        Assert.Equal(0, replica.Blend(10));
        Assert.InRange(replica.Blend(10.05f), .4999f, .5001f);
        Assert.Equal(1, replica.Blend(11));
        Assert.False(replica.Receive(Table, Layout, 3, next, 11));
        replica.Clear();
        Assert.Null(replica.Current); Assert.Null(replica.Previous);
        Assert.True(replica.Receive(Table, Layout, 3, Scene(0), 12));
    }

    [Fact]
    public void ChangeDetectionIgnoresQuaternionSignButIncludesVisibilityAndBoneMotion()
    {
        var a = Scene(count: 4); var b = Scene(count: 4);
        b.Poses[0].Rotation.W = -1;
        Assert.True(VenttiSceneReplica.Same(a, b));
        b.Poses[3].Position.Z = .002f;
        Assert.False(VenttiSceneReplica.Same(a, b));
        b.Poses[3].Position.Z = 0; b.Poses[3].Active = 0;
        Assert.False(VenttiSceneReplica.Same(a, b));
    }

    [Fact]
    public void SoundWireOrderKeepsHostVariationPositionAndDelay()
    {
        var packet = PacketCodec.Encode(Cue(3, 3));
        Assert.Equal(new byte[] { 179, 0, 4, 3, 2, 1, 2, 0, 0, 0, 3, 0, 0, 0, 7,
            0, 0, 192, 63, 0, 0, 0, 192, 0, 0, 64, 64, 0, 0, 64, 64 }, packet);
        var copy = Assert.IsType<VenttiSoundCue>(PacketCodec.Decode(packet));
        Assert.Equal(7, copy.Sound); Assert.Equal(3, copy.Delay); Assert.Equal(-2, copy.Position.Y);
        for (int length = 2; length < packet.Length; length++)
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(packet.Take(length).ToArray()));
    }

    [Fact]
    public void DelayedVoiceDoesNotBlockLaterImmediateSoundAndEachPlaysOnce()
    {
        var queue = new VenttiSoundQueue();
        Assert.True(queue.Receive(Table, Layout, 21, Cue(1, 3), 10));
        Assert.True(queue.Receive(Table, Layout, 21, Cue(2), 10));
        Assert.True(queue.TryTake(10, out var now, out float elapsed));
        Assert.Equal(2u, now!.Sequence); Assert.Equal(0, elapsed);
        Assert.False(queue.TryTake(12, out _, out _));
        Assert.True(queue.TryTake(13.25f, out var delayed, out elapsed));
        Assert.Equal(1u, delayed!.Sequence); Assert.Equal(.25f, elapsed);
        Assert.False(queue.TryTake(14, out _, out _));
        Assert.False(queue.Receive(Table, Layout, 21, Cue(2), 15));
    }

    [Fact]
    public void BindingDelayExpiresOldAudioInsteadOfReplayingItOnLateJoin()
    {
        var queue = new VenttiSoundQueue();
        queue.Receive(Table, Layout, 21, Cue(1), 0);
        queue.Receive(Table, Layout, 21, Cue(2, 3), 0);
        Assert.False(queue.TryTake(2, out _, out _));
        Assert.Equal(1, queue.Count);
        Assert.False(queue.TryTake(5, out _, out _));
        Assert.Equal(0, queue.Count);
        Assert.False(queue.Receive(Table, Layout, 21, Cue(1), 6));
        Assert.False(new VenttiSoundQueue().TryTake(100, out _, out _));
    }

    [Fact]
    public void SoundQueueRejectsInvalidDataBeforeAdvancingItsSequence()
    {
        var queue = new VenttiSoundQueue();
        queue.Receive(Table, Layout, 21, Cue(1), 0);
        var wrong = Cue(99); wrong.Sound = 21;
        Assert.False(queue.Receive(Table, Layout, 21, wrong, 1));
        wrong = Cue(99); wrong.LayoutId++;
        Assert.False(queue.Receive(Table, Layout, 21, wrong, 1));
        wrong = Cue(99); wrong.TableId++;
        Assert.False(queue.Receive(Table, Layout, 21, wrong, 1));
        foreach (float delay in new[] { float.NaN, float.PositiveInfinity, -1f, 5.01f })
            Assert.False(queue.Receive(Table, Layout, 21, Cue(99, delay), 1));
        wrong = Cue(99); wrong.Position.Z = float.NaN;
        Assert.False(queue.Receive(Table, Layout, 21, wrong, 1));
        Assert.True(queue.Receive(Table, Layout, 21, Cue(2), 1));
    }

    [Fact]
    public void CueWrapCopiesReconnectAndOverflowHaveBoundedState()
    {
        var queue = new VenttiSoundQueue();
        var original = Cue(uint.MaxValue);
        queue.Receive(Table, Layout, 21, original, 0); original.Sound = 20;
        Assert.True(queue.TryTake(0, out var copy, out _)); Assert.Equal(7, copy!.Sound);
        Assert.True(queue.Receive(Table, Layout, 21, Cue(0), 0));
        for (uint i = 1; i <= 100; i++) Assert.True(queue.Receive(Table, Layout, 21, Cue(i), 0));
        Assert.Equal(VenttiSoundQueue.Capacity, queue.Count);
        Assert.False(queue.Receive(Table, Layout, 21, Cue(0), 0));
        queue.Clear();
        Assert.Equal(0, queue.Count);
        Assert.True(queue.Receive(Table, Layout, 21, Cue(0), 0));
    }

    [Fact]
    public void PosesUseTheTransformChannelWhileSoundsStayOrderedAndHostOnly()
    {
        Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiSceneState, Channel.UnreliableSequenced));
        Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiSceneState, Channel.ReliableOrdered));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiSceneState, Channel.ReliableBulk));
        Assert.True(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiSoundCue, Channel.ReliableOrdered));
        Assert.False(SessionMessagePolicy.IsChannelAllowed(MessageId.VenttiSoundCue, Channel.UnreliableSequenced));
        foreach (var id in new[] { MessageId.VenttiSceneState, MessageId.VenttiSoundCue })
        {
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, false, true));
            Assert.False(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, false));
            Assert.True(SessionMessagePolicy.IsSenderAllowed(id, false, true, true, true));
        }
    }
}
