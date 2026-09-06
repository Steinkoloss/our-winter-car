using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests;

public sealed class ItemSpawnLifecycleTests
{
    private static ItemSpawn Manifest(bool replay = false) => new()
    {
        ContainerNetId = 123, Epoch = 1, StateName = "Spawn all", Flags = replay ? ItemSpawn.FlagReplay : (byte)0,
        Items = Enumerable.Range(1, 3).Select(i => new ItemSpawn.Entry
        {
            NetId = (uint)i, TemplateName = "chips(itemx)",
            Position = new NetVector3(i, 0, 0), Rotation = new NetQuaternion(0, 0, 0, 1),
        }).ToList(),
    };

    [Fact]
    public void RemovalBetweenReceiptAndDeferredCreationCannotResurrectAnItem()
    {
        var lifecycle = new ItemSpawnLifecycle(); var manifest = Manifest();
        Assert.True(lifecycle.AcceptManifest(manifest));
        lifecycle.Retire(2);
        var created = manifest.Items.Where(i => lifecycle.ShouldMaterialize(i.NetId, false)).Select(i => i.NetId).ToArray();
        Assert.Equal(new uint[] { 1, 3 }, created);
        Assert.True(lifecycle.AcceptManifest(Manifest(true)));
        Assert.False(lifecycle.ShouldMaterialize(2, false));
        Assert.False(lifecycle.AcceptManifest(Manifest()));
    }

    [Fact]
    public void ReplayRepairsOnlyMissingBodiesAndKeepsRetirementAcrossEveryReplay()
    {
        var lifecycle = new ItemSpawnLifecycle(); var live = new HashSet<uint>();
        void Deliver(bool replay)
        {
            var m = Manifest(replay);
            if (!lifecycle.AcceptManifest(m)) return;
            foreach (var item in m.Items)
                if (lifecycle.ShouldMaterialize(item.NetId, live.Contains(item.NetId))) Assert.True(live.Add(item.NetId));
        }
        Deliver(false); Assert.Equal(3, live.Count);
        lifecycle.Retire(1); live.Remove(1); // Consumed on the host.
        live.Remove(2); // Lost only on this guest.
        Deliver(false); Assert.Equal(new uint[] { 3 }, live.OrderBy(i => i));
        Deliver(true); Assert.Equal(new uint[] { 2, 3 }, live.OrderBy(i => i));
        for (int i = 0; i < 30; i++) Deliver(true);
        Assert.Equal(new uint[] { 2, 3 }, live.OrderBy(i => i));
        Assert.Equal(new uint[] { 1 }, lifecycle.RetiredIds);
    }

    [Fact]
    public void SnapshotRemovalBeforeAnyManifestAndNewSessionWithReusedIds()
    {
        var lifecycle = new ItemSpawnLifecycle(); lifecycle.Retire(1); lifecycle.Retire(1);
        Assert.True(lifecycle.AcceptManifest(Manifest(true)));
        Assert.False(lifecycle.ShouldMaterialize(1, false));
        lifecycle.Clear();
        Assert.Empty(lifecycle.RetiredIds); Assert.True(lifecycle.AcceptManifest(Manifest()));
        Assert.True(lifecycle.ShouldMaterialize(1, false));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("flag")]
    [InlineData("template")]
    [InlineData("empty")]
    [InlineData("position")]
    [InlineData("rotation")]
    [InlineData("zero-rotation")]
    [InlineData("count")]
    public void InvalidManifestCannotConsumeTheReceiptKey(string fault)
    {
        var lifecycle = new ItemSpawnLifecycle(); var bad = Manifest(); var entry = bad.Items[0];
        switch (fault)
        {
            case "duplicate": entry.NetId = bad.Items[1].NetId; break;
            case "flag": bad.Flags = 4; break;
            case "template": entry.TemplateName = new string('x', 129); break;
            case "empty": entry.TemplateName = ""; break;
            case "position": entry.Position = new NetVector3(float.NaN, 0, 0); break;
            case "rotation": entry.Rotation = new NetQuaternion(0, 0, 0, float.PositiveInfinity); break;
            case "zero-rotation": entry.Rotation = new NetQuaternion(); break;
            case "count": bad.Items.AddRange(Enumerable.Repeat(entry, ItemSpawn.MaxItems)); break;
        }
        bad.Items[0] = entry;
        Assert.False(lifecycle.AcceptManifest(bad));
        Assert.True(lifecycle.AcceptManifest(Manifest()));
    }

    [Fact]
    public void EmptyOfferAcknowledgmentAndDistinctSpillsStillDeduplicateIndependently()
    {
        var lifecycle = new ItemSpawnLifecycle(); var first = Manifest(); first.Items.Clear();
        Assert.True(lifecycle.AcceptManifest(first)); Assert.False(lifecycle.AcceptManifest(first));
        first.Epoch++; Assert.True(lifecycle.AcceptManifest(first));
        first.ContainerNetId++; Assert.True(lifecycle.AcceptManifest(first));
        first.Flags = ItemSpawn.FlagReplay; Assert.True(lifecycle.AcceptManifest(first));
    }

    [Fact]
    public void SeededLossRemovalAndReplayConvergeWithoutDuplicatingOrReviving()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var random = new Random(seed); var lifecycle = new ItemSpawnLifecycle();
            var local = new HashSet<uint>(); var host = new HashSet<uint> { 1, 2, 3 };
            for (int turn = 0; turn < 60; turn++)
            {
                uint id = (uint)random.Next(1, 4);
                if (random.Next(3) == 0) { host.Remove(id); lifecycle.Retire(id); local.Remove(id); }
                else local.Remove(id);
                var replay = Manifest(true);
                Assert.True(lifecycle.AcceptManifest(replay));
                foreach (var item in replay.Items)
                    if (lifecycle.ShouldMaterialize(item.NetId, local.Contains(item.NetId))) Assert.True(local.Add(item.NetId));
                Assert.Equal(host.OrderBy(i => i), local.OrderBy(i => i));
            }
        }
    }
}
