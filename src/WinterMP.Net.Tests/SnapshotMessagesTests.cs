using System.Collections.Generic;
using WinterMP.Net;
using WinterMP.Net.Messages;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class SnapshotMessagesTests
    {
        public static IEnumerable<object[]> OversizedSnapshotPackets()
        {
            yield return new object[] { MessageId.WorldDoorSnapshot, WorldDoorSnapshot.MaxEntries + 1 };
            yield return new object[] { MessageId.WorldItemSnapshot, WorldItemSnapshot.MaxEntries + 1 };
            yield return new object[] { MessageId.WorldBoltSnapshot, WorldBoltSnapshot.MaxEntries + 1 };
            yield return new object[] { MessageId.WorldPartSnapshot, WorldPartSnapshot.MaxEntries + 1 };
            yield return new object[] { MessageId.WorldItemDespawnSnapshot, WorldItemDespawnSnapshot.MaxItems + 1 };
        }

        [Theory]
        [MemberData(nameof(OversizedSnapshotPackets))]
        public void Snapshots_RejectOversizedInboundEntryCounts(MessageId messageId, int count)
        {
            var writer = new NetWriter();
            writer.WriteUInt16((ushort)messageId);
            writer.WriteUInt16((ushort)count);

            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(writer.ToArray()));
        }

        [Fact]
        public void ItemSpawn_RejectsOversizedInboundEntryCount()
        {
            var writer = new NetWriter();
            writer.WriteUInt16((ushort)MessageId.ItemSpawn);
            writer.WriteUInt32(1);
            writer.WriteUInt16(2);
            writer.WriteByte(3);
            writer.WriteString("Spawn all");
            writer.WriteUInt16((ushort)(ItemSpawn.MaxItems + 1));

            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(writer.ToArray()));
        }

        [Fact]
        public void SpawnIntent_RejectsOversizedInboundEntryCount()
        {
            var writer = new NetWriter();
            writer.WriteUInt16((ushort)MessageId.SpawnIntent);
            writer.WriteByte(1);
            writer.WriteUInt32(2);
            writer.WriteString("Spawn all");
            writer.WriteUInt16(3);
            writer.WriteUInt16((ushort)(SpawnIntent.MaxItems + 1));

            Assert.Throws<ProtocolException>(() => PacketCodec.Decode(writer.ToArray()));
        }

        [Fact]
        public void SpawnMessages_RejectOversizedOutboundEntryCounts()
        {
            var manifest = new ItemSpawn();
            var intent = new SpawnIntent();
            for (int i = 0; i <= SpawnIntent.MaxItems; i++)
            {
                manifest.Items.Add(new ItemSpawn.Entry());
                intent.Items.Add(new SpawnIntent.Entry());
            }

            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(manifest));
            Assert.Throws<ProtocolException>(() => PacketCodec.Encode(intent));
        }
    }
}
