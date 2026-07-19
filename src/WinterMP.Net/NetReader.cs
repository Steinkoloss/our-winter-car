using System;
using System.Text;

namespace WinterMP.Net
{
    /// <summary>Little-endian binary reader over a received payload. Bounds-checked.</summary>
    public sealed class NetReader
    {
        private readonly byte[] _buffer;
        private readonly int _length;
        private int _position;

        public NetReader(byte[] buffer) : this(buffer, GetBufferLength(buffer))
        {
        }

        private static int GetBufferLength(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            return buffer.Length;
        }

        public NetReader(byte[] buffer, int length)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (length < 0 || length > buffer.Length)
                throw new ProtocolException($"Invalid buffer length {length} for {buffer.Length} byte(s).");

            _buffer = buffer;
            _length = length;
        }

        public int Remaining => _length - _position;

        /// <summary>Rejects a packet whose message reader did not consume its entire payload.</summary>
        public void RequireEnd()
        {
            if (Remaining != 0)
                throw new ProtocolException($"Message has {Remaining} trailing byte(s).");
        }

        private void Require(int count)
        {
            // Compare against Remaining (never _position + count): a large positive count from a
            // malformed blob length would overflow _position + count to negative and pass the check,
            // then throw a non-ProtocolException (OOM/ArgumentException) deeper in. This keeps all
            // truncation/garbage input surfacing as a ProtocolException the dispatch layer expects.
            if (count < 0 || count > Remaining)
                throw new ProtocolException($"Message truncated: need {count} more byte(s), {Remaining} available.");
        }

        public byte ReadByte()
        {
            Require(1);
            return _buffer[_position++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            Require(4);
            uint value = (uint)(_buffer[_position]
                | (_buffer[_position + 1] << 8)
                | (_buffer[_position + 2] << 16)
                | (_buffer[_position + 3] << 24));
            _position += 4;
            return value;
        }

        public int ReadInt32() => unchecked((int)ReadUInt32());

        public ulong ReadUInt64()
        {
            ulong low = ReadUInt32();
            ulong high = ReadUInt32();
            return low | (high << 32);
        }

        public long ReadInt64() => unchecked((long)ReadUInt64());

        public unsafe float ReadSingle()
        {
            uint bits = ReadUInt32();
            return *(float*)&bits;
        }

        public string ReadString()
        {
            int byteCount = ReadUInt16();
            if (byteCount == 0) return string.Empty;
            Require(byteCount);
            string value = Encoding.UTF8.GetString(_buffer, _position, byteCount);
            _position += byteCount;
            return value;
        }

        public byte[] ReadBytes()
        {
            int count = ReadInt32();
            if (count < 0) throw new ProtocolException($"Negative blob length: {count}.");
            Require(count);
            var result = new byte[count];
            System.Array.Copy(_buffer, _position, result, 0, count);
            _position += count;
            return result;
        }

        public NetVector3 ReadVector3() => new NetVector3(ReadSingle(), ReadSingle(), ReadSingle());

        public NetQuaternion ReadQuaternion() => new NetQuaternion(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());
    }
}
