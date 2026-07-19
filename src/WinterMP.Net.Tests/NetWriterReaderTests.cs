using System;
using WinterMP.Net;
using Xunit;

namespace WinterMP.Net.Tests
{
    public class NetWriterReaderTests
    {
        [Fact]
        public void RoundTrips_AllPrimitiveTypes()
        {
            var writer = new NetWriter();
            writer.WriteByte(0xAB);
            writer.WriteBool(true);
            writer.WriteBool(false);
            writer.WriteUInt16(54321);
            writer.WriteInt32(-123456789);
            writer.WriteUInt32(0xDEADBEEF);
            writer.WriteInt64(long.MinValue);
            writer.WriteUInt64(ulong.MaxValue);
            writer.WriteSingle(3.14159f);
            writer.WriteSingle(float.NegativeInfinity);

            var reader = new NetReader(writer.ToArray());
            Assert.Equal(0xAB, reader.ReadByte());
            Assert.True(reader.ReadBool());
            Assert.False(reader.ReadBool());
            Assert.Equal(54321, reader.ReadUInt16());
            Assert.Equal(-123456789, reader.ReadInt32());
            Assert.Equal(0xDEADBEEF, reader.ReadUInt32());
            Assert.Equal(long.MinValue, reader.ReadInt64());
            Assert.Equal(ulong.MaxValue, reader.ReadUInt64());
            Assert.Equal(3.14159f, reader.ReadSingle());
            Assert.Equal(float.NegativeInfinity, reader.ReadSingle());
            Assert.Equal(0, reader.Remaining);
        }

        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("Tervetuloa talveen! ÄÖå")]
        [InlineData("emoji \U0001F697 and \u00df sharp s")]
        public void RoundTrips_Strings(string value)
        {
            var writer = new NetWriter();
            writer.WriteString(value);

            var reader = new NetReader(writer.ToArray());
            Assert.Equal(value, reader.ReadString());
        }

        [Fact]
        public void NullString_ReadsBackAsEmpty()
        {
            var writer = new NetWriter();
            writer.WriteString(null);

            var reader = new NetReader(writer.ToArray());
            Assert.Equal(string.Empty, reader.ReadString());
        }

        [Fact]
        public void RoundTrips_Blobs()
        {
            var blob = new byte[10_000];
            for (int i = 0; i < blob.Length; i++) blob[i] = (byte)(i * 31);

            var writer = new NetWriter(16); // force growth
            writer.WriteBytes(blob);

            var reader = new NetReader(writer.ToArray());
            Assert.Equal(blob, reader.ReadBytes());
        }

        [Fact]
        public void RoundTrips_VectorAndQuaternion()
        {
            var writer = new NetWriter();
            writer.WriteVector3(new NetVector3(1.5f, -2.25f, 1000f));
            writer.WriteQuaternion(new NetQuaternion(0.1f, 0.2f, 0.3f, 0.9f));

            var reader = new NetReader(writer.ToArray());
            var v = reader.ReadVector3();
            Assert.Equal(1.5f, v.X);
            Assert.Equal(-2.25f, v.Y);
            Assert.Equal(1000f, v.Z);

            var q = reader.ReadQuaternion();
            Assert.Equal(0.1f, q.X);
            Assert.Equal(0.9f, q.W);
        }

        [Fact]
        public void TruncatedPayload_ThrowsProtocolException()
        {
            var writer = new NetWriter();
            writer.WriteUInt32(42);

            var reader = new NetReader(writer.ToArray());
            reader.ReadUInt16();
            reader.ReadUInt16();
            Assert.Throws<ProtocolException>(() => reader.ReadByte());
        }

        [Fact]
        public void OversizedBlobLength_ThrowsProtocolException_NotOverflow()
        {
            // A hostile/garbage blob length must surface as ProtocolException, not an
            // overflow/OOM: the bounds check must not wrap when position + count exceeds int.MaxValue.
            var writer = new NetWriter();
            writer.WriteInt32(int.MaxValue); // claim ~2GB of blob...
            writer.WriteByte(1);             // ...but provide only one byte

            var reader = new NetReader(writer.ToArray());
            Assert.Throws<ProtocolException>(() => reader.ReadBytes());
        }

        [Fact]
        public void InvalidExplicitReaderLength_ThrowsProtocolException()
        {
            Assert.Throws<ProtocolException>(() => new NetReader(new byte[4], -1));
            Assert.Throws<ProtocolException>(() => new NetReader(new byte[4], 5));
        }

        [Fact]
        public void NullReaderBuffer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new NetReader(null!));
            Assert.Throws<ArgumentNullException>(() => new NetReader(null!, 0));
        }

        [Fact]
        public void CollectionCount16_RejectsValuesOutsideWireRange()
        {
            var writer = new NetWriter();
            Assert.Throws<ProtocolException>(() => writer.WriteCount16(-1));
            Assert.Throws<ProtocolException>(() => writer.WriteCount16(ushort.MaxValue + 1));
        }
    }
}
