using System;
using System.Collections.Generic;
using System.Linq;
using WinterMP.Net.Messages;
using WinterMP.Net.Sync;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class PartStatePolicyTests
    {
        [Theory]
        [InlineData(true, 0, false, true, NativePartPhase.Loose)]
        [InlineData(true, 1, false, true, NativePartPhase.Fitted)]
        [InlineData(true, 1, false, false, NativePartPhase.Fitted)]
        [InlineData(true, 8, false, false, NativePartPhase.Fitted)]
        [InlineData(true, 0, false, false, NativePartPhase.Unavailable)]
        [InlineData(true, -1, false, true, NativePartPhase.Unavailable)]
        [InlineData(true, 0, true, true, NativePartPhase.Retired)]
        [InlineData(true, 0, true, false, NativePartPhase.Retired)]
        [InlineData(true, 1, true, false, NativePartPhase.Retired)]
        [InlineData(false, 1, false, false, NativePartPhase.Retired)]
        [InlineData(false, 0, false, true, NativePartPhase.Retired)]
        public void NativeLifetimeUsesPersistentDataInsteadOfTemporaryPhysics(bool dataExists, int assemblyId,
            bool consumed, bool hasBody, NativePartPhase expected)
        {
            Assert.Equal(expected, PartStatePolicy.NativePhase(dataExists, assemblyId, consumed, hasBody));
        }

        [Theory]
        [InlineData(99f, .95f)]
        [InlineData(95.375f, .125f)]
        [InlineData(42.25f, 1.75f)]
        [InlineData(.00390625f, 0f)]
        [InlineData(0f, 0f)]
        [InlineData(-.0001f, -.0001f)]
        [InlineData(101f, 2f)]
        public void NativeScalarsSurviveLiveSnapshotAndRepeatedRoundTrips(float wear, float tightness)
        {
            var state = PartStatePolicy.Capture(133, true, tightness, wear)!;
            for (int trip = 0; trip < 100; trip++)
            {
                state = Assert.IsType<PartState>(PacketCodec.Decode(PacketCodec.Encode(state)));
                Assert.Equal(wear, state.WearValue); Assert.Equal(tightness, state.TightnessValue);
                var snapshot = new WorldPartSnapshot(); snapshot.Entries.Add(PartStatePolicy.SnapshotEntry(state));
                var decoded = Assert.IsType<WorldPartSnapshot>(PacketCodec.Decode(PacketCodec.Encode(snapshot)));
                Assert.True(PartStatePolicy.Valid(decoded));
                Assert.Equal(wear, decoded.Entries[0].WearValue); Assert.Equal(tightness, decoded.Entries[0].TightnessValue);
            }
        }

        [Fact]
        public void ZeroConditionAndUninstalledStateRemainARealSnapshotEntry()
        {
            var state = PartStatePolicy.Capture(133, false, 0, 0)!;
            var snapshot = new WorldPartSnapshot(); snapshot.Entries.Add(PartStatePolicy.SnapshotEntry(state));
            var decoded = Assert.IsType<WorldPartSnapshot>(PacketCodec.Decode(PacketCodec.Encode(snapshot)));
            Assert.Single(decoded.Entries); Assert.True(PartStatePolicy.Valid(decoded));
            Assert.Equal(0, decoded.Entries[0].Flags); Assert.Equal(0f, decoded.Entries[0].WearValue);
        }

        [Fact]
        public void WireRetainsTheLegacyPrefixAndAppendsFullScalars()
        {
            var state = PartStatePolicy.Capture(0x12345678, true, .125f, 95.375f)!;
            state.Tightness = 17; state.Wear = 23;
            byte[] bytes = PacketCodec.Encode(state);
            Assert.Equal(17, bytes.Length);
            Assert.Equal(new byte[] { (byte)MessageId.PartState, 0, 0x78, 0x56, 0x34, 0x12, 1, 17, 23 }, bytes.Take(9));
            var restored = Assert.IsType<PartState>(PacketCodec.Decode(bytes));
            Assert.Equal(.125f, restored.TightnessValue); Assert.Equal(95.375f, restored.WearValue);
        }

        [Fact]
        public void SnapshotAppendsScalarsAfterEveryLegacyEntryAndKeepsPairingAtChunkLimit()
        {
            var snapshot = new WorldPartSnapshot();
            for (int i = 0; i < WorldPartSnapshot.MaxEntries; i++)
                snapshot.Entries.Add(PartStatePolicy.SnapshotEntry(PartStatePolicy.Capture((uint)(i + 1), i % 2 == 0, i / 8f, 99f - i / 2f)!));
            var bytes = PacketCodec.Encode(snapshot);
            Assert.Equal(4 + 15 * WorldPartSnapshot.MaxEntries, bytes.Length);
            var prefix = new List<byte> { (byte)MessageId.WorldPartSnapshot, 0, (byte)WorldPartSnapshot.MaxEntries, 0 };
            foreach (var entry in snapshot.Entries)
            {
                prefix.Add((byte)entry.NetId); prefix.Add(0); prefix.Add(0); prefix.Add(0);
                prefix.Add(entry.Flags); prefix.Add(entry.Tightness); prefix.Add(entry.Wear);
            }
            Assert.Equal(prefix, bytes.Take(prefix.Count));
            var restored = Assert.IsType<WorldPartSnapshot>(PacketCodec.Decode(bytes));
            Assert.True(PartStatePolicy.Valid(restored));
            for (int i = 0; i < WorldPartSnapshot.MaxEntries; i++)
            {
                Assert.Equal(snapshot.Entries[i].NetId, restored.Entries[i].NetId);
                Assert.Equal(snapshot.Entries[i].TightnessValue, restored.Entries[i].TightnessValue);
                Assert.Equal(snapshot.Entries[i].WearValue, restored.Entries[i].WearValue);
            }
            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(bytes.Take(bytes.Length - 1).ToArray()));
        }

        [Theory]
        [InlineData(float.NaN, 99f)]
        [InlineData(float.PositiveInfinity, 99f)]
        [InlineData(float.NegativeInfinity, 99f)]
        [InlineData(1f, float.NaN)]
        [InlineData(1f, float.PositiveInfinity)]
        [InlineData(1f, float.NegativeInfinity)]
        public void MalformedScalarsAreRejectedBeforeCaptureApplyOrHostReply(float tightness, float wear)
        {
            Assert.Null(PartStatePolicy.Capture(133, true, tightness, wear));
            var bad = new PartState { NetId = 133, TightnessValue = tightness, WearValue = wear };
            Assert.False(PartStatePolicy.Valid(bad));
            Assert.Null(PartStatePolicy.HostReply(bad, PartStatePolicy.Capture(133, true, 1, 99)!));
            var snapshot = new WorldPartSnapshot(); snapshot.Entries.Add(PartStatePolicy.SnapshotEntry(bad));
            Assert.False(PartStatePolicy.Valid(snapshot));
        }

        [Fact]
        public void BadFlagsOrDuplicateIdsRejectTheWholeSnapshot()
        {
            var good = PartStatePolicy.Capture(133, true, 1, 99)!;
            var bad = PartStatePolicy.Capture(134, false, 0, 0)!; bad.Flags = 2;
            var snapshot = new WorldPartSnapshot();
            snapshot.Entries.Add(PartStatePolicy.SnapshotEntry(good)); snapshot.Entries.Add(PartStatePolicy.SnapshotEntry(bad));
            Assert.False(PartStatePolicy.Valid(snapshot));
            snapshot.Entries[1] = snapshot.Entries[0]; Assert.False(PartStatePolicy.Valid(snapshot));
            Assert.False(PartStatePolicy.Valid(bad));
            Assert.True(PartStatePolicy.Valid(new WorldPartSnapshot()));
        }

        [Fact]
        public void GuestReportsCannotRepairWearDamageWearOrInstallTheHostPart()
        {
            var host = PartStatePolicy.Capture(133, false, .625f, 95.375f)!;
            foreach (float guestWear in new[] { 0, 1, 99, 100, -1000, float.MaxValue })
            {
                var report = PartStatePolicy.Capture(133, true, 999, guestWear)!;
                var reply = PartStatePolicy.HostReply(report, host)!;
                Assert.Equal(host.Flags, reply.Flags); Assert.Equal(.625f, reply.TightnessValue);
                Assert.Equal(95.375f, reply.WearValue); Assert.Equal(95.375f, host.WearValue);
                Assert.NotSame(host, reply); Assert.NotSame(report, reply);
                reply.WearValue = 0; Assert.Equal(95.375f, host.WearValue);
            }
            Assert.Null(PartStatePolicy.HostReply(PartStatePolicy.Capture(134, true, 1, 99)!, host));
        }

        [Fact]
        public void DeferredReplyUsesTheNewHostStateAfterTheInteraction()
        {
            var report = PartStatePolicy.Capture(133, false, .625f, 1)!;
            // The queued native bolt/install event completes before the host samples.
            var settled = PartStatePolicy.Capture(133, true, 1, 97.5f)!;
            var reply = PartStatePolicy.HostReply(report, settled)!;
            Assert.Equal(PartState.FlagInstalled, reply.Flags); Assert.Equal(1, reply.TightnessValue);
            Assert.Equal(97.5f, reply.WearValue);
        }

        [Fact]
        public void ChecksumDetectsHealthyPartWearDifferencesAndConvergesAfterWireApply()
        {
            var host = PartStatePolicy.Capture(133, true, .125f, 95.375f)!;
            var guest = PartStatePolicy.Capture(133, true, .125f, 99f)!;
            uint Hash(PartState state) => PartStatePolicy.MixChecksum(StableHash.OffsetBasis, state);
            Assert.Equal(host.Wear, guest.Wear); // The legacy 0..1 clamp hid this difference.
            Assert.NotEqual(Hash(host), Hash(guest));
            Assert.Equal(Hash(host), Hash(Assert.IsType<PartState>(PacketCodec.Decode(PacketCodec.Encode(host)))));
            var noise = PartStatePolicy.Capture(133, true, .12500003f, 95.37501f)!;
            Assert.Equal(Hash(host), Hash(noise));
            Assert.Equal(Hash(PartStatePolicy.Capture(133, false, 0, 0)!), Hash(PartStatePolicy.Capture(133, false, -0f, -0f)!));
            Assert.NotEqual(Hash(host), Hash(PartStatePolicy.Capture(133, false, .125f, 95.375f)!));
            Assert.NotEqual(Hash(host), Hash(PartStatePolicy.Capture(133, true, .25f, 95.375f)!));
        }
    }
}
