using System;
using System.Text;

namespace WinterMP.Net
{
    /// <summary>
    /// Growable little-endian binary writer. Not thread-safe; intended to be reused
    /// (call <see cref="Reset"/>) to avoid per-message allocations on hot paths.
    /// </summary>
    public sealed class NetWriter
    {
        private byte[] _buffer;
        private int _position;

        public NetWriter(int initialCapacity = 256)
        {
            if (initialCapacity < 16) initialCapacity = 16;
            _buffer = new byte[initialCapacity];
        }

        public int Length => _position;

        /// <summary>Underlying buffer; valid up to <see cref="Length"/>. Use for zero-copy sends.</summary>
        public byte[] Buffer => _buffer;

        public void Reset() => _position = 0;

        public byte[] ToArray()
        {
            var result = new byte[_position];
            Array.Copy(_buffer, result, _position);
            return result;
        }

        private void Ensure(int count)
        {
            int required = _position + count;
            if (required <= _buffer.Length) return;
            int newSize = _buffer.Length * 2;
            while (newSize < required) newSize *= 2;
            Array.Resize(ref _buffer, newSize);
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_position++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUInt16(ushort value)
        {
            Ensure(2);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
        }

        public void WriteUInt32(uint value)
        {
            Ensure(4);
            _buffer[_position++] = (byte)value;
            _buffer[_position++] = (byte)(value >> 8);
            _buffer[_position++] = (byte)(value >> 16);
            _buffer[_position++] = (byte)(value >> 24);
        }

        public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

        public void WriteUInt64(ulong value)
        {
            WriteUInt32(unchecked((uint)value));
            WriteUInt32(unchecked((uint)(value >> 32)));
        }

        public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

        public unsafe void WriteSingle(float value)
        {
            uint bits = *(uint*)&value;
            WriteUInt32(bits);
        }

        /// <summary>UTF-8, ushort length prefix (max 65535 bytes). Null is written as empty.</summary>
        public void WriteString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteUInt16(0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > ushort.MaxValue)
                throw new ProtocolException($"String too long for wire format ({bytes.Length} bytes).");

            WriteUInt16((ushort)bytes.Length);
            Ensure(bytes.Length);
            Array.Copy(bytes, 0, _buffer, _position, bytes.Length);
            _position += bytes.Length;
        }

        /// <summary>Int32 length prefix + raw bytes. Used for blobs (snapshots, chunks).</summary>
        public void WriteBytes(byte[] value, int offset, int count)
        {
            WriteInt32(count);
            Ensure(count);
            Array.Copy(value, offset, _buffer, _position, count);
            _position += count;
        }

        public void WriteBytes(byte[] value) => WriteBytes(value, 0, value.Length);

        public void WriteVector3(NetVector3 value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
        }

        public void WriteQuaternion(NetQuaternion value)
        {
            WriteSingle(value.X);
            WriteSingle(value.Y);
            WriteSingle(value.Z);
            WriteSingle(value.W);
        }
    }
}
